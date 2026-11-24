using System.Globalization;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.Geometry;
using DH.Grading.Core;
using CivilDb = Autodesk.Civil.DatabaseServices;
using CivilStyles = Autodesk.Civil.DatabaseServices.Styles;

namespace DH.Grading.Civil;

/// <summary>★[JACK 1124 «만들어진 원지반에 표고 텍스트가 있는 스타일 · 기본 스타일하고 표고가 있는 스타일을 고를 수 있게 공통설정에»]
/// 공통설정 «원지반 표시» = 기본 | 표고 표시 — <b>도면에 저장</b>(도면 사전 XRecord «DH_GROUNDELEV» · 노리선 · 등고선 간격과 같은 길).
/// <para>표고 표시 = 지금 원지반(«원지반» · «원지반_N» TIN · 물러난 면 빼고 · 숨김 상관없음)마다 <b>Civil 등고선 라벨</b>(SurfaceContourLabelGroup) — 계곡선만 · 종이 100 mm마다 ·
/// 라벨 스타일 «DH_원지반 표고»(글 2.5 mm · DH-한글 · 바탕 가림) · 도면층 «DH-원지반-표고» · XData «DH_GELEV» = 그 원지반 핸들.
/// 자리 = 우리 세모로 그은 계곡선(<see cref="ContourLabels"/> — 배치 평면도 ⑥ 등고값과 같은 한 벌) · 자리마다 계곡선을 가로지르는 짧은 라벨 선(선은 안 보임).</para>
/// <para>지표면 스타일은 안 건드린다 — 계곡선 간격 = 그 원지반 스타일의 값(<see cref="ContourInterval"/>이 고치는 값).</para>
/// <para><see cref="Sync"/> — 원지반을 만들거나 바꾸는 명령 끝에서 부른다. 꺼져 있으면 기록 한 번 읽고 끝 · 켜져 있으면 원지반마다 지문(판 번호 · 점 수 · 표고 · 간격)과 수가 같으면 그대로 ·
/// 바뀐 원지반만 다시 · 없어진 원지반 라벨은 지움 · 축척이 바뀌면 전부 다시. 실패해도 명령은 안 깨짐(진단 로그 한 줄).</para></summary>
internal static class GroundElevLabels
{
    const string Key = "DH_GROUNDELEV";
    internal const string App = "DH_GELEV";
    internal const string LayerName = "DH-원지반-표고";
    internal const string StyleName = "DH_원지반 표고";
    const double TextMm = 2.5, SpacingMm = 100, MinLenMm = 30, ProbeMm = 2, HalfLineMm = 0.3;
    /// <summary>안쪽에서 여러 번 짓는 명령(측량도 보정 · 도로 다듬기)이 중간 Sync를 건너뛰게 — 끝에서 한 번.</summary>
    internal static int Hold;

    /// <summary>켜짐(표고 표시)인가 — 기록이 없으면 꺼짐(기본 = 지금까지 그대로).</summary>
    internal static bool IsOn(Database db)
    {
        try
        {
            using var tr = db.TransactionManager.StartTransaction();
            var (on, _, _) = ReadRec(tr, db);
            tr.Commit();
            return on;
        }
        catch { return false; }
    }

    static (bool On, string Sig, int Count) ReadRec(Transaction tr, Database db)
    {
        var nod = (DBDictionary)tr.GetObject(db.NamedObjectsDictionaryId, OpenMode.ForRead);
        if (!nod.Contains(Key) || tr.GetObject(nod.GetAt(Key), OpenMode.ForRead) is not Xrecord xr || xr.Data == null) return (false, "", 0);
        var v = xr.Data.AsArray();
        bool on = v.Length > 0 && v[0].Value is short s && s != 0;
        string sig = v.Length > 1 && v[1].Value is string g ? g : "";
        int n = v.Length > 2 && v[2].Value is int c ? c : 0;
        return (on, sig, n);
    }

    static void WriteRec(Transaction tr, Database db, bool on, string sig, int count)
    {
        var nod = (DBDictionary)tr.GetObject(db.NamedObjectsDictionaryId, OpenMode.ForRead);
        Xrecord xr;
        if (nod.Contains(Key)) xr = (Xrecord)tr.GetObject(nod.GetAt(Key), OpenMode.ForWrite);
        else
        {
            nod.UpgradeOpen();
            xr = new Xrecord();
            nod.SetAt(Key, xr);
            tr.AddNewlyCreatedDBObject(xr, true);
        }
        xr.Data = new ResultBuffer(new TypedValue((int)DxfCode.Int16, (short)(on ? 1 : 0)),
                                   new TypedValue((int)DxfCode.Text, sig),
                                   new TypedValue((int)DxfCode.Int32, count));
    }

    /// <summary>[저장] 뒤 — 켬/끔을 적고 바로 맞춘다(끄면 다 지움 · 켜면 지금 원지반에). 돌려줌 = 명령창 한 줄. 명령 안(문서 잠김)에서.</summary>
    internal static string Set(Database db, bool on)
    {
        try
        {
            using (var tr = db.TransactionManager.StartTransaction()) { WriteRec(tr, db, on, "", 0); tr.Commit(); }
            if (!on) { int n = Remove(db); return $"원지반 표시 기본 — 표고 {n}개 지움"; }
            return "원지반 표시 표고 — " + Sync(db);
        }
        catch (System.Exception ex) { Log("저장 실패 — " + ex.Message); return "⚠원지반 표시 저장 실패 — " + ex.Message; }
    }

    /// <summary>우리 라벨(XData «DH_GELEV») 모두 지움 — 지운 수.</summary>
    internal static int Remove(Database db)
    {
        int n = 0;
        using var tr = db.TransactionManager.StartTransaction();
        foreach (var id in Ours(tr, db).Keys)
            try { tr.GetObject(id, OpenMode.ForWrite, false, true).Erase(); n++; } catch { }
        tr.Commit();
        return n;
    }

    /// <summary>우리 라벨 → 주인 원지반 핸들(모형 공간).</summary>
    internal static Dictionary<ObjectId, string> Ours(Transaction tr, Database db)
    {
        var r = new Dictionary<ObjectId, string>();
        var ms = (BlockTableRecord)tr.GetObject(SymbolUtilityServices.GetBlockModelSpaceId(db), OpenMode.ForRead);
        Autodesk.AutoCAD.Runtime.RXClass? cls = null;
        try { cls = Autodesk.AutoCAD.Runtime.RXObject.GetClass(typeof(CivilDb.SurfaceContourLabelGroup)); } catch { }
        foreach (ObjectId id in ms)
        {
            if (cls != null && !id.ObjectClass.IsDerivedFrom(cls)) continue;   // 등고선 라벨 묶음만 연다(큰 도면에서 싸게)
            try
            {
                if (tr.GetObject(id, OpenMode.ForRead) is not Entity e) continue;
                using var rb = e.GetXDataForApplication(App);
                if (rb == null) continue;
                string h = "";
                foreach (var tv in rb) if (tv.TypeCode == (int)DxfCode.ExtendedDataHandle || tv.TypeCode == (int)DxfCode.ExtendedDataAsciiString) h = tv.Value as string ?? "";
                r[id] = h;
            }
            catch { }
        }
        return r;
    }

    readonly record struct Target(ObjectId Id, string Handle, ObjectId StyleId, double Major, string Print);

    /// <summary>원지반을 만들거나 바꾸는 명령 끝에서 — 꺼져 있으면 아무것도 안 함. 돌려줌 = 한 줄(바꾼 것이 없으면 빈 글). 예외를 안 던진다.
    /// <para>대상 = 지금 원지반(«원지반» · «원지반_N» TIN · 물러난 면 아님) — 숨김은 상관없음(결과지표면만 표시 · 보기 전환으로 숨겨도 그 원지반이 지금 원지반).
    /// 원지반마다 지문(판 번호 · 점 수 · 표고 · 계곡선 간격)과 라벨 수를 기록 — 바뀐 원지반만 다시 · 없어진 원지반 라벨은 지움 · 축척이 바뀌면 전부 다시.</para></summary>
    internal static string Sync(Database db)
    {
        if (Hold > 0) return "";
        var sw = System.Diagnostics.Stopwatch.StartNew();
        try
        {
            bool on; string sig0; int n0;
            using (var tr = db.TransactionManager.StartTransaction()) { (on, sig0, n0) = ReadRec(tr, db); tr.Commit(); }
            if (!on) return "";

            double scale = Commands.SheetCommand.CurrentDrawingScale(db);
            if (!(scale > 0)) scale = 1000;   // DH가 축척을 건 적 없는 도면 = 1:1000
            string scaleTag = string.Format(CultureInfo.InvariantCulture, "1:{0:0}", scale);
            var cdoc = Autodesk.Civil.ApplicationServices.CivilDocument.GetCivilDocument(db);
            var tg = new List<Target>();
            var haveBy = new Dictionary<string, List<ObjectId>>();
            using (var tr = db.TransactionManager.StartTransaction())
            {
                foreach (ObjectId sid in cdoc.GetSurfaceIds())
                    try
                    {
                        if (sid.IsErased || tr.GetObject(sid, OpenMode.ForRead) is not CivilDb.TinSurface ts) continue;
                        if (!Commands.SectionCommand.IsBase(ts.Name ?? "", Commands.ImportGisCommand.GroundSurfaceName) || SurfaceRetire.IsRetired(ts)) continue;
                        double major = 0;
                        try { if (!ts.StyleId.IsNull && tr.GetObject(ts.StyleId, OpenMode.ForRead) is CivilStyles.SurfaceStyle st) major = st.ContourStyle.MajorContourInterval; } catch { }
                        if (!(major > 0)) major = ContourInterval.Read(db).Major;
                        string pr = "?";
                        try { var g = ts.GetGeneralProperties(); pr = string.Format(CultureInfo.InvariantCulture, "{0}:{1}:{2:F3}:{3:F3}:{4:F3}", g.RevisionNumber, g.NumberOfPoints, g.MinimumElevation, g.MaximumElevation, g.MeanElevation); } catch { }
                        tg.Add(new Target(sid, sid.Handle.ToString(), ts.StyleId, major, pr));
                    }
                    catch { }
                foreach (var kv in Ours(tr, db)) { if (!haveBy.TryGetValue(kv.Value, out var l)) haveBy[kv.Value] = l = new(); l.Add(kv.Key); }
                tr.Commit();
            }
            tg.Sort((a, b) => string.CompareOrdinal(a.Handle, b.Handle));
            string Item(Target t) => string.Format(CultureInfo.InvariantCulture, "{0}:{1}:{2:0.###}", t.Handle, t.Print, t.Major);

            // 지난 기록 «항목=수;…|1:축척» — 축척이 같고 항목 · 실제 라벨 수가 같은 원지반은 그대로
            var old = new Dictionary<string, int>();
            string[] sp = sig0.Split('|');
            bool sameScale = sp.Length == 2 && sp[1] == scaleTag;
            if (sameScale) foreach (var it in sp[0].Split(';', StringSplitOptions.RemoveEmptyEntries)) { int e = it.LastIndexOf('='); if (e > 0 && int.TryParse(it[(e + 1)..], out int c)) old[it[..e]] = c; }
            var keep = new HashSet<string>();
            foreach (var t in tg)
            {
                string it = Item(t);
                int now = haveBy.TryGetValue(t.Handle, out var l) ? l.Count : 0;
                if (!it.Contains('?') && old.TryGetValue(it, out int c) && c == now) keep.Add(t.Handle);
            }
            bool stray = haveBy.Keys.Any(h => !tg.Any(t => t.Handle == h));
            if (keep.Count == tg.Count && !stray)
            {
                if (haveBy.Count > 0) using (var tr = db.TransactionManager.StartTransaction()) { MatchVisibility(tr, db); tr.Commit(); }   // 바뀐 것이 없어도 보임만은 맞춤(같으면 안 씀)
                return "";
            }

            // 바뀐 원지반 · 없어진 원지반(지워짐 · 물러남 · 이름 바뀜) 라벨 지움
            int gone = 0;
            using (var tr = db.TransactionManager.StartTransaction())
            {
                foreach (var kv in haveBy)
                {
                    if (keep.Contains(kv.Key)) continue;
                    foreach (var id in kv.Value) try { tr.GetObject(id, OpenMode.ForWrite, false, true).Erase(); gone++; } catch { }
                }
                tr.Commit();
            }
            double m = scale / 1000.0;   // 종이 1 mm = 모형 m
            int bad = 0, empty = 0, total = 0;
            var per = new List<string>();
            var items = new List<string>();
            var made = new Dictionary<string, List<ObjectId>>();
            using (var tr = db.TransactionManager.StartTransaction())
            {
                var lay = Commands.SectionCommand.EnsureLayer(db, tr, LayerName, 8);
                GradingBuilder.EnsureRegApp(db, tr, App);
                foreach (var t in tg)
                {
                    if (keep.Contains(t.Handle)) continue;
                    var stId = EnsureStyle(db, tr, cdoc, t.Major);
                    if (stId.IsNull) { per.Add("⚠라벨 스타일 못 만듦"); break; }
                    var lines = ContourLabels.Lines(EarthworkVolume.Tris(db, t.Id), t.Major);
                    var labs = ContourLabels.Place(lines, SpacingMm * m, MinLenMm * m, ProbeMm * m);
                    double h = HalfLineMm * m;
                    var mine = made[t.Handle] = new List<ObjectId>();
                    foreach (var q in labs)
                    {
                        var gid = ObjectId.Null;
                        try
                        {
                            double nx = -Math.Sin(q.AngleRad) * h, ny = Math.Cos(q.AngleRad) * h;
                            var pts = new Point2dCollection { new Point2d(q.X - nx, q.Y - ny), new Point2d(q.X + nx, q.Y + ny) };
                            gid = CivilDb.SurfaceContourLabelGroup.Create(t.Id, pts, stId, stId, stId);
                            if (gid.IsNull) { bad++; continue; }
                            var lg = (CivilDb.SurfaceContourLabelGroup)tr.GetObject(gid, OpenMode.ForWrite, false, true);   // 지금 도면층이 잠겨 있어도
                            try { lg.IsMinorContourLabelVisible = false; } catch { }
                            try { lg.IsUserContourLabelVisible = false; } catch { }
                            try { lg.IsMajorContourLabelVisible = true; } catch { }
                            try { lg.IsLabelLineVisible = false; } catch { }
                            lg.LayerId = lay;
                            lg.XData = new ResultBuffer(new TypedValue((int)DxfCode.ExtendedDataRegAppName, App),
                                                        new TypedValue((int)DxfCode.ExtendedDataHandle, t.Handle));
                            mine.Add(gid);
                        }
                        catch
                        {
                            bad++;
                            if (!gid.IsNull) try { tr.GetObject(gid, OpenMode.ForWrite, false, true).Erase(); } catch { }   // 표시 못 단 라벨은 남기지 않음
                        }
                    }
                }
                tr.Commit();
            }
            // 라벨 선이 Civil 계곡선과 안 만난 묶음(라벨 0개 — 경계 · 평탄부 끝) 지움 — 커밋 뒤라야 Civil이 셈
            using (var tr = db.TransactionManager.StartTransaction())
            {
                foreach (var kv in made)
                    for (int i = kv.Value.Count - 1; i >= 0; i--)
                        try
                        {
                            var lg = (CivilDb.SurfaceContourLabelGroup)tr.GetObject(kv.Value[i], OpenMode.ForRead);
                            if (lg.SubEntityCount > 0) continue;
                            lg.UpgradeOpen(); lg.Erase(); kv.Value.RemoveAt(i); empty++;
                        }
                        catch { }
                foreach (var t in tg)
                {
                    int c = keep.Contains(t.Handle) ? haveBy[t.Handle].Count : made.TryGetValue(t.Handle, out var l) ? l.Count : 0;
                    total += c;
                    items.Add(Item(t) + "=" + c.ToString(CultureInfo.InvariantCulture));
                    if (!keep.Contains(t.Handle)) per.Add(string.Format(CultureInfo.InvariantCulture, "{0} 계곡선 {1:0.##} m · {2}개", NameOf(tr, t.Id), t.Major, c));
                }
                WriteRec(tr, db, true, string.Join(";", items) + "|" + scaleTag, total);
                MatchVisibility(tr, db);   // 숨긴 원지반(결과지표면만 표시)에 단 라벨은 숨김
                tr.Commit();
            }
            string line = (tg.Count == 0 ? "원지반 없음" : per.Count == 0 ? "그대로" : string.Join(" · ", per))
                        + (gone > 0 ? $" · 지난 표고 {gone}개 지움" : "") + (bad > 0 ? $" · ⚠못 단 자리 {bad}" : "");
            Log($"{scaleTag} · {line} · 빈 묶음 {empty} 지움 · 그대로 둔 원지반 {keep.Count} · {sw.ElapsedMilliseconds} ms");
            return line;
        }
        catch (System.Exception ex)
        {
            Log("⚠" + ex.Message);
            return "⚠원지반 표고 못 맞춤 — " + ex.Message;
        }
    }

    /// <summary>우리 라벨 보임 = 주인 원지반 보임 — 지표면 보임을 바꾸는 한 벌(<see cref="GradingBuilder.IsolateSurfaces(Transaction, string?, Func{CivilDb.Surface, bool}?)"/> · SetSurfaceVisible · 보기 전환 되돌림)이
    /// 같은 트랜잭션 끝에서 부른다(결과지표면만 표시 · [보기]로 원지반을 숨기면 표고도 숨김). 꺼져 있으면 기록 한 번 읽고 끝 · 같으면 안 씀 · 예외를 안 던진다.</summary>
    internal static void MatchVisibility(Transaction tr, Database? db)
    {
        if (db == null) return;
        try
        {
            if (!ReadRec(tr, db).On) return;
            var vis = new Dictionary<string, bool>();
            foreach (var kv in Ours(tr, db))
            {
                if (!vis.TryGetValue(kv.Value, out bool v))
                {
                    v = true;
                    try { if (db.TryGetObjectId(new Handle(Convert.ToInt64(kv.Value, 16)), out var sid) && !sid.IsErased && tr.GetObject(sid, OpenMode.ForRead) is Entity se) v = se.Visible; } catch { }
                    vis[kv.Value] = v;
                }
                var e = (Entity)tr.GetObject(kv.Key, OpenMode.ForRead);
                if (e.Visible == v) continue;
                e.UpgradeOpen();
                e.Visible = v;
            }
        }
        catch (System.Exception ex) { Log("보임 맞춤 ⚠" + ex.Message); }
    }

    static string NameOf(Transaction tr, ObjectId id) { try { return (tr.GetObject(id, OpenMode.ForRead) as CivilDb.Surface)?.Name ?? ""; } catch { return ""; } }

    static void Log(string s) { try { DiagLog.Append($"\n■ {DateTime.Now:HH:mm:ss} 원지반 표고 — {s}\n"); } catch { } }

    /// <summary>라벨 스타일 «DH_원지반 표고» — 없으면 만들고 · 있으면 글(자릿수 = 간격) · 높이 · 글꼴을 맞춤(같으면 안 씀).</summary>
    static ObjectId EnsureStyle(Database db, Transaction tr, Autodesk.Civil.ApplicationServices.CivilDocument cdoc, double major)
    {
        var col = cdoc.Styles.LabelStyles.SurfaceLabelStyles.ContourLabelStyles;
        ObjectId id = ObjectId.Null;
        foreach (ObjectId sid in col)
            try { if (tr.GetObject(sid, OpenMode.ForRead) is CivilStyles.LabelStyle s0 && string.Equals(s0.Name, StyleName, StringComparison.OrdinalIgnoreCase)) { id = sid; break; } } catch { }
        if (id.IsNull) id = col.Add(StyleName);
        if (id.IsNull) return id;
        var st = (CivilStyles.LabelStyle)tr.GetObject(id, OpenMode.ForWrite);
        int dec = 0;
        while (dec < 3 && Math.Abs(major * Math.Pow(10, dec) - Math.Round(major * Math.Pow(10, dec))) > 1e-6) dec++;
        // ★[화면 없는 Civil 실측 1124] 속성 이름은 Civil 언어판 이름으로 읽고 쓴다 — 한국어판 «지표면 표고»(영문 «Surface Elevation»을 넣으면 글이 «<Surface Elevation>» 그대로)
        //   → 이 도면의 다른 등고선 라벨 스타일 글에서 이름을 읽어 씀 · 없으면 언어판 이름
        string prop = PropName(tr, col, id);
        string contents = $"<[{prop}(Um|P{dec}|RN|AP|Sn|OF)]>";
        var kst = Commands.ImportGisCommand.EnsureKoreanTextStyle(db, tr);
        try
        {
            if (!kst.IsNull && tr.GetObject(kst, OpenMode.ForRead) is TextStyleTableRecord ks)
            { using var p = st.Properties; using var lb = p.Label; using var ts = lb.TextStyle; if (ts.Value != ks.Name) ts.Value = ks.Name; }
        }
        catch { }
        try { using var p = st.Properties; using var pr = p.PlanReadability; using var r = pr.PlanReadable; if (!r.Value) r.Value = true; } catch { }
        var comps = st.GetComponents(CivilStyles.LabelStyleComponentType.Text);
        if (comps.Count == 0) { st.AddComponent("표고", CivilStyles.LabelStyleComponentType.Text); comps = st.GetComponents(CivilStyles.LabelStyleComponentType.Text); }
        foreach (ObjectId cid in comps)
            try
            {
                if (tr.GetObject(cid, OpenMode.ForWrite) is not CivilStyles.LabelStyleTextComponent tc) continue;
                using (var tx = tc.Text)
                {
                    using (var c = tx.Contents) if (c.Value != contents) c.Value = contents;
                    using (var hgt = tx.Height) if (Math.Abs(hgt.Value - TextMm / 1000.0) > 1e-9) hgt.Value = TextMm / 1000.0;
                    try { using var at = tx.Attachment; at.Value = Autodesk.Civil.LabelTextAttachmentType.MiddleCenter; } catch { }
                }
                try { using var g = tc.General; using var ap = g.AnchorLocation; ap.Value = Autodesk.Civil.AnchorPointType.MiddleCenter; } catch { }
                try { using var bd = tc.Border; using var bm = bd.BackgroundMask; if (!bm.Value) bm.Value = true; using var bv = bd.Visible; if (bv.Value) bv.Value = false; } catch { }
            }
            catch { }
        return id;
    }

    /// <summary>등고선 라벨 «표고» 속성 이름(언어판 이름) — 다른 등고선 라벨 스타일 글 «&lt;[이름(…)]&gt;»에서 · 없으면 한국어판이면 «지표면 표고» · 아니면 «Surface Elevation».</summary>
    static string PropName(Transaction tr, CivilStyles.LabelStyleCollection col, ObjectId self)
    {
        foreach (ObjectId sid in col)
            try
            {
                if (sid == self || tr.GetObject(sid, OpenMode.ForRead) is not CivilStyles.LabelStyle s) continue;
                foreach (ObjectId cid in s.GetComponents(CivilStyles.LabelStyleComponentType.Text))
                {
                    if (tr.GetObject(cid, OpenMode.ForRead) is not CivilStyles.LabelStyleTextComponent tc) continue;
                    string v; using (var tx = tc.Text) using (var c = tx.Contents) v = c.Value ?? "";
                    var mt = System.Text.RegularExpressions.Regex.Match(v, @"<\[([^\(\]\[<>]+)\(U[a-z]+\|");
                    if (mt.Success) return mt.Groups[1].Value.Trim();
                }
            }
            catch { }
        return CultureInfo.CurrentUICulture.TwoLetterISOLanguageName == "ko" ? "지표면 표고" : "Surface Elevation";
    }
}
