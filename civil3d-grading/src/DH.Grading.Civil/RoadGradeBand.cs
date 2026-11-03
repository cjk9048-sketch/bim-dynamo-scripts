using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.Geometry;
using DH.Grading.Civil.Commands;
using DH.Grading.Core;
using CivilDb = Autodesk.Civil.DatabaseServices;
using CS = Autodesk.Civil.DatabaseServices.Styles;

namespace DH.Grading.Civil;

/// <summary>★★★[v111.7.2 · JACK 1102 «구배 글씨가 나머지 밴드랑 달라 — 색상도 다르고 · 구배가 측점별로 나오는데 종단선형이 바뀌는 구간까지 한 번에 — PVI가 두 번 꺾였으면 구간 3개 → 구배 3개만»]
/// 진입도로 종단도 구배 칸을 <b>합친 구간</b>으로 직접 그린다.
/// <para>Civil 종단 기하 밴드는 접선(PVI 사이)마다 라벨을 달고 한 줄로 이어진 접선을 못 합친다 — 진입도로는 주측점마다 PVI가 있어(v111.2 «주측점 PVI» — 끌기용 · 그대로 둠) «S=12.00% / L=20.00»이 20 m마다 되풀이됐다.
/// 그래서 밴드 칸(행 · 제목 · 높이)은 Civil 것을 그대로 두고 접선 라벨 · 선 · 눈금은 스타일에서 끈 뒤(<see cref="ProfileCommand.EnsureRoadGradeStyle"/>),
/// 보이는 구배(0.01 %)가 같은 이웃 접선을 하나로 묶어(<see cref="ProfileSheetRules.GradeRuns"/>) 구간마다 대각선(오르막 ╱ · 내리막 ╲ — Civil 그림과 같은 자리) · 위 «S=x.xx%» · 아래 «L=xx.xx»(합친 수평 길이)를 그린다.</para>
/// <para>글자는 옆 칸(횡단 데이터 «계획고» 칸) 값 라벨과 같은 레이어 · 색 · 글꼴 스타일 · 글꼴 · 높이 — 그 라벨 스타일에서 읽는다. 선 · 눈금은 꺼 둔 Civil 표시 스타일의 레이어 · 색을 그대로 쓴다.
/// 그린 것은 평범한 도면 객체라 종단도 짓기가 표식을 달아(<see cref="RoadSheets.Build"/>) 다시 그릴 때 함께 지워진다. 여러 장이면 장마다 제 측점 범위로 잘라 보이는 부분 가운데에 글자.</para></summary>
internal static class RoadGradeBand
{
    /// <summary>Civil 대각선의 위 · 아래 띄움(칸 높이 비) — JACK 화면(1102 15:17)에서 잰 값: 칸 62 px 중 위 6 · 아래 6.5 px.</summary>
    const double LineInset = 0.10;

    static int VgIndex(CivilDb.ProfileViewBandItemCollection items)
    {
        for (int i = 0; i < items.Count; i++)
            try { if (items[i].BandType == Autodesk.Civil.BandType.VerticalGeometry) return i; } catch { }
        return -1;
    }

    /// <summary>옆 칸 — 횡단 데이터 칸 중 «계획고»(없으면 첫 횡단 데이터 칸) 스타일.</summary>
    static CS.SectionalDataBandStyle? Sibling(Transaction tr, CivilDb.ProfileViewBandItemCollection items, OpenMode mode)
    {
        CS.SectionalDataBandStyle? first = null;
        for (int i = 0; i < items.Count; i++)
        {
            try
            {
                if (items[i].BandType != Autodesk.Civil.BandType.SectionalData) continue;
                if (tr.GetObject(items[i].BandStyleId, mode) is not CS.SectionalDataBandStyle sd) continue;
                first ??= sd;
                if ((sd.Name ?? "").Contains("계획고", StringComparison.Ordinal)) return sd;
            }
            catch { }
        }
        return first;
    }

    static CS.LabelStyleTextComponent? FirstText(Transaction tr, ObjectId lsId, OpenMode mode)
    {
        if (lsId.IsNull) return null;
        if (tr.GetObject(lsId, OpenMode.ForRead) is not CS.LabelStyle ls) return null;
        foreach (ObjectId cid in ls.GetComponents(CS.LabelStyleComponentType.Text))
            if (tr.GetObject(cid, mode) is CS.LabelStyleTextComponent tc) return tc;
        return null;
    }

    /// <summary>★[v111.7.2] 구배 칸 제목 · 테두리를 옆 칸과 같게 — 테두리 · 제목상자 · 제목 글자 표시(색 · 레이어) · 제목 글자(가로 · 색 · 높이).
    /// 구배 칸이 없으면(부지 종단도) 아무것도 안 한다. <see cref="SheetCommand"/> 밴드 정리 안(스타일을 쓰기로 연 트랜잭션)에서 부른다.</summary>
    internal static void StyleRow(Transaction tr, CivilDb.ProfileViewBandItemCollection items, System.Text.StringBuilder log)
    {
        int iv = VgIndex(items);
        if (iv < 0) return;
        try
        {
            if (tr.GetObject(items[iv].BandStyleId, OpenMode.ForWrite) is not CS.VerticalGeometryBandStyle vg) return;
            var sib = Sibling(tr, items, OpenMode.ForRead);
            if (sib == null) { log.AppendLine("   [구배] ⚠옆 횡단 데이터 칸이 없어 제목 · 테두리를 못 맞춤"); return; }
            int nDs = 0;
            foreach (var (mine, theirs) in new[]
            {
                (CS.VerticalGeometryDisplayStyleType.Border, CS.SectionalDataDisplayStyleType.Border),
                (CS.VerticalGeometryDisplayStyleType.TitleBox, CS.SectionalDataDisplayStyleType.TitleBox),
                (CS.VerticalGeometryDisplayStyleType.TitleBoxText, CS.SectionalDataDisplayStyleType.TitleBoxText),
            })
            {
                try
                {
                    using var a = vg.GetDisplayStylePlan(mine);
                    using var b = sib.GetDisplayStylePlan(theirs);
                    a.Color = b.Color; a.Layer = b.Layer; a.Visible = true;
                    nDs++;
                }
                catch (System.Exception ex) { log.AppendLine($"   [구배] ⚠{mine} 표시 못 맞춤 — " + ex.Message); }
            }
            // 제목 글자 — 옆 칸 제목 글자의 색 · 높이 · 가로(각 0)
            string tNote;
            // ★[v111.7.2 검토 낮음] 제목 라벨 스타일을 다른 종단 기하 밴드 스타일(회사 템플릿 원본 등)과 나눠 쓰면 안 고침 — 원본 제목이 바뀌지 않게
            bool shared = false;
            try
            {
                var cdocS = Autodesk.Civil.ApplicationServices.CivilDocument.GetCivilDocument(vg.Database);
                foreach (ObjectId sid in cdocS.Styles.BandStyles.ProfileViewVerticalGeometryBandStyles)
                    if (sid != vg.ObjectId && tr.GetObject(sid, OpenMode.ForRead) is CS.VerticalGeometryBandStyle o && o.TitleTextLabelStyleId == vg.TitleTextLabelStyleId) { shared = true; break; }
            }
            catch { }
            var mineT = shared ? null : FirstText(tr, vg.TitleTextLabelStyleId, OpenMode.ForWrite);
            var theirT = FirstText(tr, sib.TitleTextLabelStyleId, OpenMode.ForRead);
            if (shared) tNote = "⚠제목 라벨 스타일을 다른 스타일과 나눠 써서 안 고침";
            else if (mineT == null || theirT == null) tNote = "⚠제목 글자 구성요소 없음";
            else
            {
                using (var tx = mineT.Text)
                using (var ty = theirT.Text)
                {
                    using (var ang = tx.Angle) ang.Value = 0;
                    using (var c0 = tx.Color) using (var c1 = ty.Color) c0.Value = c1.Value;
                    using (var h0 = tx.Height) using (var h1 = ty.Height) h0.Value = h1.Value;
                }
                tNote = "가로 · 색 · 높이 = 옆 칸";
            }
            log.AppendLine($"   [구배] 칸 {iv} 제목 · 테두리 = 옆 칸 «{sib.Name}» — 표시 {nDs}/3 · 제목 글자 {tNote}");
        }
        catch (System.Exception ex) { log.AppendLine("   [구배] ⚠제목 · 테두리 맞추기 실패 — " + ex.Message); }
    }

    /// <summary>★★[v111.7.2] 구배 칸 그리기 — 합친 구간마다 대각선 · «S=» · «L=» · 끝 눈금(꺼 둔 Civil 눈금 스타일 그대로 — 구간 끝에만). 돌려줌 = 이 뷰에 그린 구간 수(−1 = 구배 칸 없음 · 못 그림).
    /// <para>부르는 곳 = <see cref="SheetCommand.Build"/> 끝(진입도로만) — 뷰 크기 · 밴드 높이가 정해진 뒤.</para></summary>
    internal static int Draw(Database db, ObjectId pvId, ObjectId planId, double scale, System.Text.StringBuilder log)
    {
        try
        {
            using var tr = db.TransactionManager.StartTransaction();
            var pv = (CivilDb.ProfileView)tr.GetObject(pvId, OpenMode.ForRead);
            int iv;
            CS.VerticalGeometryBandStyle? vg = null;
            CS.SectionalDataBandStyle? sib;
            using (var items = pv.Bands.GetBottomBandItems())
            {
                iv = VgIndex(items);
                if (iv >= 0) vg = tr.GetObject(items[iv].BandStyleId, OpenMode.ForRead) as CS.VerticalGeometryBandStyle;
                sib = Sibling(tr, items, OpenMode.ForRead);
            }
            if (iv < 0 || vg == null) { tr.Commit(); log.AppendLine("   [구배] 구배 칸 없음 — 안 그림"); return -1; }
            var plan = (CivilDb.Profile)tr.GetObject(planId, OpenMode.ForRead);
            var pvi = new List<(double, double)>();
            foreach (CivilDb.ProfilePVI q in plan.PVIs) pvi.Add((q.RawStation, q.Elevation));
            var runs = ProfileSheetRules.GradeRuns(pvi);
            // ★[도로 마무리 ① · 계획 5.8] 빈 구간(부지 바닥) — 그 칸은 경사 글자 대신 «부지 바닥 구간»(대각선 없음) · 끝 맞춤 꺼진 도로는 그대로
            var gapsB = new List<(double A, double B)>();
            try
            {
                var dB = RoadDesign.Read(tr, pv.AlignmentId);
                if (dB != null && dB.EndsOn) { var rgB = RoadRanges.Of(tr, pv.AlignmentId, dB); for (int i = 1; i < rgB.Count; i++) gapsB.Add((rgB[i - 1].B, rgB[i].A)); }
            }
            catch { }

            // 칸 자리 — 그래프 상자 아래 «틈» 띄우고 칸마다 같은 높이(SheetCommand 제목칸 꾸미기와 같은 셈)
            double mm = scale / 1000.0, cell = SheetCommand.BandCellMm * mm;
            var ext = pv.GeometricExtents;
            double yT = ext.MinPoint.Y - SheetCommand.TopGapMm * mm - iv * cell, yB = yT - cell, m = LineInset * cell;
            double vs0 = pv.StationStart, vs1 = pv.StationEnd;
            double xa = 0, xb = 0, ya = 0;
            bool okA = pv.FindXYAtStationAndElevation(vs0, pv.ElevationMin, ref xa, ref ya);
            bool okB = pv.FindXYAtStationAndElevation(vs1, pv.ElevationMin, ref xb, ref ya);
            if (!okA) xa = ext.MinPoint.X;
            if (!okB || !(vs1 > vs0)) xb = xa + (vs1 - vs0);   // 가로는 측점 1 m = 모형 1 m
            double X(double st) => xa + (st - vs0) * (xb - xa) / Math.Max(1e-9, vs1 - vs0);

            // 글자 — 옆 칸 값 라벨(«측점 값» 글자)의 레이어 · 색 · 글꼴 스타일 · 글꼴 · 높이
            var (layT, colT, styT, hT, pre, post) = TextLook(tr, db, pv, sib, mm);
            // 선 · 눈금 — 꺼 둔 Civil 표시 스타일 그대로(레이어가 도면에 없으면 뷰 레이어)
            var (layL, colL) = Look(tr, db, pv, vg, CS.VerticalGeometryDisplayStyleType.TangentGeometry);
            var (layK, colK) = Look(tr, db, pv, vg, CS.VerticalGeometryDisplayStyleType.Ticks);

            var ms = (BlockTableRecord)tr.GetObject(SymbolUtilityServices.GetBlockModelSpaceId(db), OpenMode.ForWrite);
            // ★[v111.7.2 검토 중간 4] 도면 기본값(SetDatabaseDefaults) 뒤에 레이어 · 색 · 글꼴 스타일 · 높이 — 앞에 넣으면 지금 TEXTSTYLE · TEXTSIZE가 이겨 옆 칸 글씨와 달라짐
            void Add(Entity e, ObjectId lay, Autodesk.AutoCAD.Colors.Color c)
            {
                e.SetDatabaseDefaults(db);
                if (e is MText mt) { mt.TextStyleId = styT; mt.TextHeight = hT; }
                e.LayerId = lay; e.Color = c;
                ms.AppendEntity(e); tr.AddNewlyCreatedDBObject(e, true);
            }
            // ★[v111.7.2 검토 중간 3] 글자는 이 장의 이름 측점 범위 안에만 — 뷰 범위에는 둘째 장부터 앞 1 cm(SheetLeadM) · 끝 여백(TailPadModel)이 붙어 있어,
            //   거기에 걸린 구간 조각에 S= · L=를 달면 장 가장자리에서 제목칸 · 밴드 밖으로 튀어나감. 보이는 조각이 글자 폭보다 짧으면 글자는 빼고 선 · 눈금만
            double s0Road = pvi.Count > 0 ? pvi.Min(q => q.Item1) : vs0;
            double na = vs0 + (vs0 > s0Road + 1e-6 ? SheetCommand.SheetLeadM : 0), nb = vs1 - SheetCommand.TailPadModel(scale);
            if (!(nb > na)) { na = vs0; nb = vs1; }
            int nRun = 0, nTick = 0, nText = 0;
            var ticks = new HashSet<(long, int)>();
            foreach (var r in runs)
            {
                double a = Math.Max(r.S0, vs0), b = Math.Min(r.S1, vs1);
                if (!(b - a > 1e-6)) continue;
                // 대각선 — 합친 구간 전체의 모서리 → 모서리(위 · 아래 띄움)를 이 뷰 범위로 자름
                double y0, y1;
                if (r.GradePct > 0) { y0 = yB + m; y1 = yT - m; }
                else if (r.GradePct < 0) { y0 = yT - m; y1 = yB + m; }
                else { y0 = y1 = (yT + yB) / 2; }   // 0.00 % — 가운데 가로선(내 판단 · Civil 템플릿엔 평탄 예가 없음)
                double Y(double st) => y0 + (y1 - y0) * (st - r.S0) / Math.Max(1e-9, r.S1 - r.S0);
                bool inGap = gapsB.Any(g => r.S0 >= g.A - 1e-3 && r.S1 <= g.B + 1e-3);
                if (!inGap) Add(new Line(new Point3d(X(a), Y(a), 0), new Point3d(X(b), Y(b), 0)), layL, colL);
                nRun++;
                string sTxt = inGap ? "부지 바닥 구간" : $"S={Math.Abs(r.GradePct):0.00}%", lTxt = $"L={r.S1 - r.S0:0.00}";
                double la = Math.Max(r.S0, na), lb = Math.Min(r.S1, nb);
                if (X(lb) - X(la) >= LabelWidth(Math.Max(sTxt.Length, lTxt.Length), hT))
                {
                    double xc = (X(la) + X(lb)) / 2;
                    Add(new MText { Contents = pre + sTxt + post, Attachment = AttachmentPoint.TopCenter, Location = new Point3d(xc, yT, 0) }, layT, colT);
                    Add(new MText { Contents = pre + lTxt + post, Attachment = AttachmentPoint.BottomCenter, Location = new Point3d(xc, yB, 0) }, layT, colT);
                    nText++;
                }
                // 끝 눈금 — 꺼 둔 오르막 · 내리막 눈금 스타일(위 · 가운데 · 아래 짧은 눈금 · 전체 높이)을 구간 끝에만
                CS.BandTickStyle? tk = null;
                try { tk = r.GradePct < 0 ? vg.DownhillTangentTickStyle : vg.UphillTangentTickStyle; } catch { }
                if (tk == null) continue;
                foreach (double st in new[] { r.S0, r.S1 })
                {
                    if (st < vs0 - 1e-6 || st > vs1 + 1e-6) continue;
                    double x = X(st);
                    void Tick(int kind, double ya2, double yb2)
                    {
                        if (!ticks.Add(((long)Math.Round(x * 1000), kind))) return;
                        Add(new Line(new Point3d(x, ya2, 0), new Point3d(x, yb2, 0)), layK, colK); nTick++;
                    }
                    try
                    {
                        if (tk.IncrementTicksToFullHeight) Tick(0, yB, yT);
                        if (tk.IncrementSmallTicksAtBottom) Tick(1, yB, yB + tk.SmallTicksAtBottomSize * 1000 * mm);
                        if (tk.IncrementSmallTicksAtTop) Tick(2, yT, yT - tk.SmallTicksAtTopSize * 1000 * mm);
                        if (tk.IncrementSmallTicksAtMiddle) { double h2 = tk.SmallTicksAtMiddleSize * 1000 * mm / 2, yc = (yT + yB) / 2; Tick(3, yc - h2, yc + h2); }
                    }
                    catch { }
                }
            }
            tr.Commit();
            log.AppendLine($"   [구배] 칸 {iv} — PVI {pvi.Count}개 · 접선 {Math.Max(0, pvi.Count - 1)}개 → 합친 구간 {runs.Count}개 · 이 뷰(측점 {vs0:0.##}~{vs1:0.##}) {nRun}개 · 글자 {nText}쌍(이름 범위 {na:0.##}~{nb:0.##}) · 눈금 {nTick}개 · 글자 높이 {hT / mm:0.##}mm");
            return nRun;
        }
        catch (System.Exception ex)
        {
            log.AppendLine("   [구배] ⚠구배 칸 그리기 실패 — " + ex.Message);
            try { DiagLog.Append("\n[구배] 그리기 실패 — " + ex); } catch { }
            return -1;
        }
    }

    /// <summary>★★[도로 마무리 ① · 계획_도로마무리.md 5.6 · 5.8] 빈 구간(부지 바닥) — 종단도 그래프의 계획선을 그 구간만 <b>점선</b> + 위에 «부지 바닥 구간».
    /// <para>Civil 종단선은 구간마다 선 꼴을 못 바꾼다 → 그 구간 계획선 위를 배경 가림 띠(MText 배경 가림 · 종이 0.8 mm 높이 · 선만 가림)로 덮고
    /// 같은 자리에 점선(DASHED — 횡단 · 종단 구조물 점선과 같은 무늬 크기) · 색 · 레이어 = 계획 종단 스타일의 선. 그리기 차례 = 가림 띠 → 점선 → 글자(맨 위).</para>
    /// <para>끝 맞춤 꺼진 도로 · 빈 구간 없는 도로는 아무것도 안 그림(지금과 같음). 그린 것은 평범한 도면 객체라 종단도 짓기의 표식으로 다시 그릴 때 지워진다.
    /// 돌려줌 = 이 뷰에 그린 빈 구간 수(−1 = 못 그림). 부르는 곳 = <see cref="SheetCommand.Build"/> 끝(진입도로만 · 구배 칸 다음).</para></summary>
    internal static int DrawGaps(Database db, ObjectId pvId, ObjectId planId, double scale, System.Text.StringBuilder log)
    {
        try
        {
            List<(double A, double B)> gaps = new();
            using (var tr0 = db.TransactionManager.StartTransaction())
            {
                var pv0 = (CivilDb.ProfileView)tr0.GetObject(pvId, OpenMode.ForRead);
                var d = RoadDesign.Read(tr0, pv0.AlignmentId);
                if (d != null && d.EndsOn) { var rg = RoadRanges.Of(tr0, pv0.AlignmentId, d); for (int i = 1; i < rg.Count; i++) gaps.Add((rg[i - 1].B, rg[i].A)); }
                tr0.Commit();
            }
            if (gaps.Count == 0) return 0;
            var (ltName, lts) = WallSectionDraw.Prepare(db);   // 점선 선종류 싣기(제 트랜잭션) — 아래 트랜잭션 앞에서
            var made = new List<ObjectId>();
            int n = 0;
            using (var tr = db.TransactionManager.StartTransaction())
            {
                var pv = (CivilDb.ProfileView)tr.GetObject(pvId, OpenMode.ForRead);
                var plan = (CivilDb.Profile)tr.GetObject(planId, OpenMode.ForRead);
                double mm = scale / 1000.0, vs0 = pv.StationStart, vs1 = pv.StationEnd;
                // 계획선 모양 — 스타일 «선»의 레이어(도면에 없으면 종단 레이어) · 색
                ObjectId lay = plan.LayerId;
                var col = Autodesk.AutoCAD.Colors.Color.FromColorIndex(Autodesk.AutoCAD.Colors.ColorMethod.ByLayer, 256);
                try
                {
                    var st = (CS.ProfileStyle)tr.GetObject(plan.StyleId, OpenMode.ForRead);
                    using var ds = st.GetDisplayStyleProfile(CS.ProfileDisplayStyleProfileType.Line);
                    var lt = (LayerTable)tr.GetObject(db.LayerTableId, OpenMode.ForRead);
                    string ln = ds.Layer ?? "";
                    if (ln.Length > 0 && ln != "0" && lt.Has(ln)) lay = lt[ln];
                    col = ds.Color;
                }
                catch { }
                // 글자 — 구배 칸과 같은 글꼴 스타일 · 높이(옆 칸 값 라벨)
                CS.SectionalDataBandStyle? sib;
                using (var items = pv.Bands.GetBottomBandItems()) sib = Sibling(tr, items, OpenMode.ForRead);
                var (_, _, styT, hT, pre, post) = TextLook(tr, db, pv, sib, mm);
                ObjectId ltId = ObjectId.Null;
                if (ltName != null) { var ltt = (LinetypeTable)tr.GetObject(db.LinetypeTableId, OpenMode.ForRead); if (ltt.Has(ltName)) ltId = ltt[ltName]; }
                var ms = (BlockTableRecord)tr.GetObject(SymbolUtilityServices.GetBlockModelSpaceId(db), OpenMode.ForWrite);
                ObjectId Add(Entity e)
                {
                    e.SetDatabaseDefaults(db);
                    e.LayerId = lay; e.Color = col;
                    var id = ms.AppendEntity(e); tr.AddNewlyCreatedDBObject(e, true);
                    made.Add(id);
                    return id;
                }
                // 계획선에 곡선(종단 곡선)이 있으면 0.5 m마다 · 아니면 PVI 자리만(직선 접선 — 뷰에서도 곧음)
                bool curved = false;
                try { foreach (CivilDb.ProfileEntity pe in plan.Entities) if (pe.EntityType != CivilDb.ProfileEntityType.Tangent) { curved = true; break; } } catch { curved = true; }
                var pvis = new List<double>();
                foreach (CivilDb.ProfilePVI q in plan.PVIs) pvis.Add(q.RawStation);
                foreach (var g in gaps)
                {
                    double a = Math.Max(g.A, vs0), b = Math.Min(g.B, vs1);
                    if (!(b - a > 1e-6)) continue;
                    var ss = new SortedSet<double> { a, b };
                    foreach (double s in pvis) if (s > a + 1e-6 && s < b - 1e-6) ss.Add(s);
                    if (curved) for (double s = a + 0.5; s < b - 1e-6; s += 0.5) ss.Add(s);
                    var pts = new List<Point2d>();
                    foreach (double s in ss)
                    {
                        double x = 0, y = 0;
                        try { if (pv.FindXYAtStationAndElevation(s, plan.ElevationAt(s), ref x, ref y)) pts.Add(new Point2d(x, y)); } catch { }
                    }
                    if (pts.Count < 2) { log.AppendLine($"   [빈 구간] ⚠측점 {a:0.##}~{b:0.##} 계획선 자리를 못 얻음 — 안 그림"); continue; }
                    // ① 가림 띠(선분마다) — MText 배경 가림(도면 배경색 · 종이에는 흰색) · 너비 = 선분 길이 · 높이 = 종이 0.8 mm
                    for (int i = 0; i + 1 < pts.Count; i++)
                    {
                        var p = pts[i]; var q = pts[i + 1]; double len = p.GetDistanceTo(q);
                        if (len < 1e-6) continue;
                        Add(new MText
                        {
                            Contents = " ", TextHeight = 0.8 * mm, Width = len, Attachment = AttachmentPoint.MiddleCenter,
                            Location = new Point3d((p.X + q.X) / 2, (p.Y + q.Y) / 2, 0), Rotation = Math.Atan2(q.Y - p.Y, q.X - p.X),
                            BackgroundFill = true, UseBackgroundColor = true, BackgroundScaleFactor = 1.0,
                        });
                    }
                    // ② 점선
                    var pl = new Polyline(pts.Count);
                    for (int i = 0; i < pts.Count; i++) pl.AddVertexAt(i, pts[i], 0, 0, 0);
                    Add(pl);
                    if (!ltId.IsNull) { pl.LinetypeId = ltId; if (lts > 0) pl.LinetypeScale = lts; }
                    // ③ 글자 — 보이는 조각 가운데 · 선 위 종이 1.5 mm
                    var mid = new Point2d((pts[0].X + pts[^1].X) / 2, (pts[0].Y + pts[^1].Y) / 2);
                    string txt = "부지 바닥 구간";
                    if (pts[^1].X - pts[0].X >= LabelWidth(txt.Length * 2, hT))   // 한글 = 숫자 두 자 폭 어림
                    {
                        var mt = new MText { Contents = pre + txt + post, Attachment = AttachmentPoint.BottomCenter, Location = new Point3d(mid.X, mid.Y + 1.5 * mm, 0) };
                        Add(mt);
                        mt.TextStyleId = styT; mt.TextHeight = hT;
                    }
                    n++;
                    log.AppendLine($"   [빈 구간] 측점 {g.A:0.##}~{g.B:0.##}(이 뷰 {a:0.##}~{b:0.##}) — 계획선 점선 {pts.Count}점 · 선종류 {ltName ?? "없음(실선)"} · 무늬 {lts:0.###}");
                }
                tr.Commit();
            }
            // 그리기 차례 — 막 만든 것은 다른 트랜잭션에서 맨 위로(DrawOrderFix와 같은 법) · 만든 차례 그대로(가림 띠 → 점선 → 글자)
            if (made.Count > 0)
                try
                {
                    using var tr2 = db.TransactionManager.StartTransaction();
                    var ms2 = (BlockTableRecord)tr2.GetObject(SymbolUtilityServices.GetBlockModelSpaceId(db), OpenMode.ForRead);
                    var dot = (DrawOrderTable)tr2.GetObject(ms2.DrawOrderTableId, OpenMode.ForWrite);
                    foreach (var id in made) dot.MoveToTop(new ObjectIdCollection { id });
                    tr2.Commit();
                }
                catch (System.Exception exO) { log.AppendLine("   [빈 구간] ⚠그리기 차례 못 맞춤 — " + exO.Message); }
            return n;
        }
        catch (System.Exception ex)
        {
            log.AppendLine("   [빈 구간] ⚠계획선 점선 못 그림 — " + ex.Message);
            try { DiagLog.Append("\n[빈 구간] 점선 그리기 실패 — " + ex); } catch { }
            return -1;
        }
    }

    /// <summary>★[v111.7.2 검토 중간 3] 구배 글자 한 줄 폭 어림(모형) — 맑은 고딕 숫자 · 영문 ≈ 높이 0.62배 + 양옆 여유 높이 0.5배. 이보다 짧은 조각엔 글자를 안 단다.</summary>
    internal static double LabelWidth(int chars, double h) => h * (0.62 * chars + 0.5);

    /// <summary>표시 스타일의 레이어(도면에 없으면 뷰 레이어) · 색.</summary>
    static (ObjectId Layer, Autodesk.AutoCAD.Colors.Color Color) Look(Transaction tr, Database db, CivilDb.ProfileView pv, CS.VerticalGeometryBandStyle vg, CS.VerticalGeometryDisplayStyleType t)
    {
        ObjectId lay = pv.LayerId;
        var col = Autodesk.AutoCAD.Colors.Color.FromColorIndex(Autodesk.AutoCAD.Colors.ColorMethod.ByLayer, 256);
        try
        {
            using var ds = vg.GetDisplayStylePlan(t);
            var lt = (LayerTable)tr.GetObject(db.LayerTableId, OpenMode.ForRead);
            string ln = ds.Layer ?? "";
            if (ln.Length > 0 && ln != "0" && lt.Has(ln)) lay = lt[ln];
            col = ds.Color;
        }
        catch { }
        return (lay, col);
    }

    /// <summary>옆 칸 값 라벨의 모양 — 레이어(그 칸 라벨 묶음이 앉은 레이어 · 못 찾으면 뷰 레이어) · 색(글자 색 · 따름이면 표시 색) · 글꼴 스타일 · 높이(모형) · 글꼴 감쌈(«{\f…;» … «}»).</summary>
    static (ObjectId Layer, Autodesk.AutoCAD.Colors.Color Color, ObjectId Style, double H, string Pre, string Post) TextLook(
        Transaction tr, Database db, CivilDb.ProfileView pv, CS.SectionalDataBandStyle? sib, double mm)
    {
        ObjectId lay = pv.LayerId, sty = db.Textstyle;
        var col = Autodesk.AutoCAD.Colors.Color.FromColorIndex(Autodesk.AutoCAD.Colors.ColorMethod.ByAci, 3);
        double h = 2.5 * mm; string pre = "", post = "";
        if (sib == null) return (lay, col, sty, h, pre, post);
        try
        {
            var ls = (CS.LabelStyle)tr.GetObject(sib.SampleLineStationLabelStyleId, OpenMode.ForRead);
            try
            {
                string tsn = ls.Properties.Label.TextStyle.Value ?? "";
                var tt = (TextStyleTable)tr.GetObject(db.TextStyleTableId, OpenMode.ForRead);
                if (tsn.Length > 0 && tt.Has(tsn)) sty = tt[tsn];
            }
            catch { }
            var tc = FirstText(tr, sib.SampleLineStationLabelStyleId, OpenMode.ForRead);
            if (tc != null)
            {
                using var tx = tc.Text;
                using (var hp = tx.Height) if (hp.Value > 0) h = hp.Value * 1000 * mm;   // 종이 m → 모형
                using (var cp = tx.Color) col = cp.Value;
                using (var ct = tx.Contents)
                {
                    var mt = System.Text.RegularExpressions.Regex.Match(ct.Value ?? "", @"^(\{\\f[^;]*;)");
                    if (mt.Success) { pre = mt.Groups[1].Value; post = "}"; }
                }
            }
            if (col.IsByLayer || col.IsByBlock)
                try { using var ds = sib.GetDisplayStylePlan(CS.SectionalDataDisplayStyleType.SampleLineStationLabels); col = ds.Color; } catch { }
        }
        catch { }
        // 레이어 — 그 칸 라벨 묶음(횡단 데이터 밴드 라벨)이 이 뷰 밴드 자리(가로 범위 × 그래프 아래 끝 ~ 아래 밴드 높이) 안에 앉은 레이어
        //   ★[JACK 1103] 세로 아래 끝도 본다 — 같은 X 아래에 놓인 남의 종단도 묶음의 레이어를 따라가지 않게
        try
        {
            var ext = pv.GeometricExtents;
            int nb = 0;
            try { using var bi = pv.Bands.GetBottomBandItems(); nb = bi.Count; } catch { }
            double yMin = ext.MinPoint.Y - (SheetCommand.TopGapMm + (nb + 1) * SheetCommand.BandCellMm) * mm;
            var ms = (BlockTableRecord)tr.GetObject(SymbolUtilityServices.GetBlockModelSpaceId(db), OpenMode.ForRead);
            foreach (ObjectId id in ms)
            {
                DBObject o; try { o = tr.GetObject(id, OpenMode.ForRead); } catch { continue; }
                if (o is not CivilDb.LabelGroup lg || !o.GetType().Name.Contains("SectionalDataBand", StringComparison.Ordinal) || lg.SubEntityCount == 0) continue;
                var p = lg.GetAt(0).LabelLocation;
                if (p.X < ext.MinPoint.X - 1 || p.X > ext.MaxPoint.X + 1 || p.Y > ext.MinPoint.Y || p.Y < yMin) continue;
                lay = lg.LayerId;
                break;
            }
        }
        catch { }
        return (lay, col, sty, h, pre, post);
    }
}
