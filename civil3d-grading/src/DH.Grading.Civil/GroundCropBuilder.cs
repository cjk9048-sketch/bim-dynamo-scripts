using Autodesk.AutoCAD.ApplicationServices;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.Geometry;
using DH.Grading.Core;
using Box = DH.Grading.Core.GroundCrop.Box;
using CivilDb = Autodesk.Civil.DatabaseServices;
using Tri = DH.Grading.Core.WallDaylight.Tri;

namespace DH.Grading.Civil;

/// <summary>★★★[v111.9 · 계획_원지형자르기.md 3 ~ 5장 · JACK 1103 «어느 단계에서나 수행하면 모든 지표면이 잘렸으면»] <b>원지형 자르기 본체</b> — 상자 밖 지표면을 <b>모두</b> 숨긴다(Hide 네 띠 · 핸들 · 이름 · 스타일 · 레이어 그대로).
/// <list type="number">
/// <item><b>읽기만</b>: 면 가르기(땅 크기 · 설계 크기 · 건너뜀) · 보이는 세모 범위 · 설계 자리 D(구역 · 진입도로 · 옹벽 · 터파기 · 노선 · 표본선 · 설계 크기 면) → «상자 ⊇ D + 5 m» 아니면 <b>거절</b>(도면 0줄 — 임시 객체도 안 만듦 · 1027 메모).</item>
/// <item>대상 = 보이는 범위가 상자를 넘는 땅 크기 면 + 지난 자르기 띠가 든 면 · 딸린 면 = 대상을 붙여넣은 스냅샷 없는 면(V86 «(옛)» — 안 다시 지으면 구식으로 남음) · 체적면.</item>
/// <item><b>바깥 트랜잭션</b> 안에서 ① 우리 옛 띠 빼고 새 네 띠 ② 소스 먼저 재작성(스냅샷 재작성은 안 부름 — 1013) ③ 확인(안쪽 지문 · 표본 높이 · 세모 &gt; 0 · 구식 그대로) → 어긋나면 <b>바깥 물림</b>(V86 (vi′) — 모든 면 비트 같음 · DBMOD 0) · 맞으면 기록 쓰고 커밋.</item>
/// <item>최종지표면_DH가 자르기 전 «맞음»이면 같은 명령에서 다시 지음(<see cref="FinalSurfaceBuilder.Build"/> — 최종면은 한 곳에서만 짓는다) · 다시 키운 상자면 못 커진 면 이름.</item>
/// </list></summary>
internal static class GroundCropBuilder
{
    const string DictName = "DH_GRADING";
    const string RecName = "CROP";
    /// <summary>표본 높이 격자(한 변 점 수) — D 안 25 × 25.</summary>
    const int GridN = 25;
    /// <summary>★[V87 측정 1103] 표본 높이 «같음»의 끝(m) — 띠가 가른 긴 세모 안의 점은 <b>같은 평면</b>을 쪼갠 작은 세모로 다시 셈해 끝자리가 달라진다(시험 도면: 625점 중 17점 · 최대 3.1e-13 m).
    /// 세모를 다시 짠 것(뒤집힘 · 점 더함)은 mm ~ m 차이라 이 끝(1 nm)에 안 걸린다 · 안쪽 세모 지문은 비트 그대로 견준다(<see cref="GroundCrop.InsideHash"/>).</summary>
    const double HeightEps = 1e-9;

#pragma warning disable CS0649   // 시험(반사)만 씀
    /// <summary>화면 없는 Civil 시험 고리 — 참이면 확인(5장 6)을 일부러 실패시켜 되돌림 길을 밟는다(V87 fail).</summary>
    internal static bool FailVerifyForTest;
    /// <summary>시험 고리 — 참이면 첫 대상 면의 재작성을 일부러 실패시킨다(V87 rebuildfail · 설치 전 검토 H1).</summary>
    internal static bool FailRebuildForTest;
    /// <summary>시험 고리 — 참이면 첫 대상 면에 띠를 안 넣는다(상자 밖 세모가 남음 → 확인이 잡아야 함 · V87 nobands).</summary>
    internal static bool SkipBandsForTest;
#pragma warning restore CS0649
    /// <summary>시험 고리 — 지난 실행 결과(숫자 · 빨간 항목 · 줄).</summary>
    internal static Result? LastCropStats;

    internal enum Kind { Ground, Design, Skip, Volume, Other }

    /// <summary>실행 결과.</summary>
    internal sealed class Result
    {
        public bool Ok, Refused, RolledBack, Nothing;
        public string Why = "", Line = "", Done = "", FinalNote = "", GrowNote = "";
        public GroundCrop.Cover? Cover;
        public readonly List<string> Red = new();
        public int Targets, Dependents, Untouched, Users, SameInside, Checked, HeightDiff, Stale;
        /// <summary>스냅샷 없는 붙여넣기 면 · 체적면(대상 + 딸린 면) — 다음 명령(DHCROPDEPS)에서 다시 지을 것.</summary>
        public int Chain;
        public long Ms, FinalMs;
        /// <summary>표본 높이 — 끝자리가 다른 점 수(면 합) · 가장 큰 차(m).</summary>
        public int HeightBits; public double HeightMax;
        public readonly List<string> NotGrown = new();
        public readonly List<string> Skipped = new();
    }

    /// <summary>면 하나(읽기 패스).</summary>
    sealed class Surf
    {
        public ObjectId Id;
        public string Name = "", Handle = "";
        public Kind K;
        public bool User, IsGround, Snap, Ood0, Target, Dep;
        public Box Vis = Box.None, All = Box.None, Ext0 = Box.None;
        /// <summary>안쪽 지문을 잴 네모 — 처음 자르기 = 상자 · 지난 띠가 든 면 = 상자 ∩ 지난 상자(다시 키우면 지난 상자 밖은 새로 드러나는 자리라 견줄 앞이 없음).</summary>
        public Box HashBox = Box.None;
        public List<Tri>? Tris;
        public int Tri0, Tri1;
        public string Hash0 = "";
        public double[] Heights0 = Array.Empty<double>();
        public readonly List<ObjectId> Sources = new();
        /// <summary>정의가 붙여넣기 줄뿐인가(경계 · 점 · 선 없음).</summary>
        public bool OnlyPaste;
        public List<Box> OldBands = new(), NewBands = new();
        public int Removed;
        public long RebuildMs;
        public string Rebuilt = "";
    }

    /// <summary>지난 자르기 기록 — 상자 · 면마다 넣은 띠 · 처음 자르기 전 범위.</summary>
    sealed class Rec
    {
        public Box Box = Box.None;
        public readonly Dictionary<string, (List<Box> Bands, Box Ext0)> Surf = new(StringComparer.OrdinalIgnoreCase);
    }

    static bool OurName(string nm) => nm.Contains("_DH") || nm.StartsWith("DH_", StringComparison.Ordinal);

    /// <summary>★[계획 3장] 면 가르기 — <b>땅 크기</b>(자르는 면: 원지반 · 사람 면 · 정지면_DH… · 터파기 붙여넣기 · 보기 합성 · 지층 · 지하수위 · 최종면) ·
    /// <b>설계 크기</b>(그 밖의 우리 면 — 손 안 대고 설계 자리 D에 듦: 가상* · 정지순수 · 옹벽 · 진입도로 …) · 건너뜀(토량 임시 · 짓는중) · 체적면 · 격자면.
    /// 모르는 우리 이름은 설계 크기로 본다 — 틀리면 거절(아무것도 안 바뀜) 쪽으로 틀리게.</summary>
    internal static Kind Classify(CivilDb.Surface s)
    {
        if (s is CivilDb.TinVolumeSurface || s is CivilDb.GridVolumeSurface) return Kind.Volume;
        if (s is not CivilDb.TinSurface) return Kind.Other;
        string nm = s.Name ?? "";
        if (nm == "_DH토량임시" || nm.Contains("짓는중") || nm == RoadSession.TempSurf) return Kind.Skip;
        if (FinalSurfaceBuilder.IsRoadSurface(s)) return Kind.Design;
        if (!OurName(nm)) return Kind.Ground;
        if (nm.StartsWith(Commands.SectionCommand.PadSurfaceBase, StringComparison.Ordinal)
            || nm.StartsWith(Commands.CreateGradingCommand.StashBase, StringComparison.Ordinal)
            || nm.StartsWith(StrataDraw.SurfPrefix, StringComparison.Ordinal)) return Kind.Ground;
        foreach (var b in new[] { Commands.ExcavCommand.SurfName, Commands.ExcavCommand.BaseName, Commands.ViewSurfaceCommand.AllName, Commands.ViewSurfaceCommand.ExcavAllName,
                                  FinalSurfaceBuilder.Name, FinalSurfaceBuilder.OldName, Commands.WallCompositeCommand.OldCompName, StrataDraw.WaterSurfName })
            if (Commands.SectionCommand.IsBase(nm, b)) return Kind.Ground;
        return Kind.Design;
    }

    // ══════════════════════════════ 기록 ══════════════════════════════

    static Rec ReadRec(Database db, Transaction tr)
    {
        var r = new Rec();
        try
        {
            var nod = (DBDictionary)tr.GetObject(db.NamedObjectsDictionaryId, OpenMode.ForRead);
            if (!nod.Contains(DictName)) return r;
            var d = (DBDictionary)tr.GetObject(nod.GetAt(DictName), OpenMode.ForRead);
            if (!d.Contains(RecName)) return r;
            var xr = (Xrecord)tr.GetObject(d.GetAt(RecName), OpenMode.ForRead);
            using var rb = xr.Data;
            if (rb == null) return r;
            var a = rb.AsArray().Select(v => v.Value as string ?? "").ToArray();
            if (a.Length > 0 && GroundCrop.TryBox(a[0], out var b)) r.Box = b;
            for (int i = 2; i < a.Length; i++)
            {
                var p = a[i].Split('|');
                if (p.Length < 3) continue;
                GroundCrop.TryBox(p[2], out var e0);
                r.Surf[p[0]] = (GroundCrop.ParseBands(p[1]), e0);
            }
        }
        catch { }
        return r;
    }

    static void WriteRec(Database db, Transaction tr, Box box, IEnumerable<Surf> cropped)
    {
        var nod = (DBDictionary)tr.GetObject(db.NamedObjectsDictionaryId, OpenMode.ForRead);
        DBDictionary d;
        if (!nod.Contains(DictName))
        {
            nod.UpgradeOpen();
            d = new DBDictionary();
            nod.SetAt(DictName, d);
            tr.AddNewlyCreatedDBObject(d, true);
        }
        else d = (DBDictionary)tr.GetObject(nod.GetAt(DictName), OpenMode.ForWrite);
        var vals = new List<TypedValue>
        {
            new((int)DxfCode.Text, GroundCrop.BoxText(box)),
            new((int)DxfCode.Text, $"{DateTime.Now:yyyy-MM-dd HH:mm:ss} · {GradingSettings.Version}"),
        };
        foreach (var s in cropped) vals.Add(new((int)DxfCode.Text, $"{s.Handle}|{GroundCrop.BandsText(s.NewBands)}|{GroundCrop.BoxText(s.Ext0)}"));
        if (d.Contains(RecName)) d.Remove(RecName);
        using var rb = new ResultBuffer(vals.ToArray());
        var xr = new Xrecord { Data = rb };
        d.SetAt(RecName, xr);
        tr.AddNewlyCreatedDBObject(xr, true);
    }

    /// <summary>★[계획 6장 첫 줄] 이 면에 우리 자르기 띠가 들었나(기록에 핸들 · 띠가 있음) — 정지옵션 [저장] · 이름별 재작성이 «스냅샷 재작성 → 구식이면 재작성 한 번 더»를 할지(자르지 않은 도면은 늘 거짓 — 0줄 차이).</summary>
    internal static bool IsCropped(Transaction tr, DBObject s)
    {
        try
        {
            var r = ReadRec(s.Database, tr);
            return r.Surf.TryGetValue(s.Handle.ToString(), out var e) && e.Bands.Count > 0;
        }
        catch { return false; }
    }

    /// <summary>지난 자르기 기록에 든 면(살아 있는 것만) — DHCROPDEPS의 소스 씨앗.</summary>
    internal static HashSet<ObjectId> RecordedIds(Database db)
    {
        var res = new HashSet<ObjectId>();
        try
        {
            using var tr = db.TransactionManager.StartTransaction();
            foreach (var h in ReadRec(db, tr).Surf.Keys)
                try { if (db.TryGetObjectId(new Handle(Convert.ToInt64(h, 16)), out var id) && !id.IsErased) res.Add(id); } catch { }
            tr.Commit();
        }
        catch { }
        return res;
    }

    /// <summary>★[v111.9 · 설치 전 검토 M1] 아직 구식인 면 이름(새 트랜잭션에서 잼) — DHCROPDEPS 끝 확인.</summary>
    internal static List<string> StaleOf(Database db, IEnumerable<ObjectId> ids)
    {
        var res = new List<string>();
        try
        {
            using var tr = db.TransactionManager.StartTransaction();
            foreach (var id in ids.Distinct())
                try { if (!id.IsErased && tr.GetObject(id, OpenMode.ForRead) is CivilDb.Surface s && s.IsOutOfDate) res.Add(s.Name); } catch { }
            tr.Commit();
        }
        catch { }
        return res;
    }

    /// <summary>DHRESET(좌표계 변경 — 원지반까지 지움)에서 기록을 지운다.</summary>
    internal static bool ClearRecord(Database db, Transaction tr)
    {
        try
        {
            var nod = (DBDictionary)tr.GetObject(db.NamedObjectsDictionaryId, OpenMode.ForRead);
            if (!nod.Contains(DictName)) return false;
            var d = (DBDictionary)tr.GetObject(nod.GetAt(DictName), OpenMode.ForWrite);
            if (!d.Contains(RecName)) return false;
            d.Remove(RecName);
            return true;
        }
        catch { return false; }
    }

    // ══════════════════════════════ 읽기 도우미 ══════════════════════════════

    static Point3dCollection RectPts(Box b) => new() { new Point3d(b.X0, b.Y0, 0), new Point3d(b.X1, b.Y0, 0), new Point3d(b.X1, b.Y1, 0), new Point3d(b.X0, b.Y1, 0) };

    /// <summary>경계 목록(정의 차례) — 한 줄에 여러 고리가 든 경계는 고리마다(색인은 그 줄).</summary>
    static List<(int Op, GroundCrop.Bnd B)> Bounds(CivilDb.TinSurface s)
    {
        var res = new List<(int, GroundCrop.Bnd)>();
        var bd = s.BoundariesDefinition;
        for (int i = 0; i < bd.Count; i++)
        {
            using var op = bd[i];
            string ty = op.BoundaryType.ToString();
            if (op.Count != 1) { res.Add((i, new GroundCrop.Bnd(ty + "(여럿)", Array.Empty<(double, double)>()))); continue; }
            var v = new List<(double X, double Y)>();
            foreach (Point3d p in op[0].Vertices) v.Add((p.X, p.Y));
            res.Add((i, new GroundCrop.Bnd(ty, v)));
        }
        return res;
    }

    /// <summary>D 안 표본 높이(25 × 25) — 면 밖은 NaN.</summary>
    static double[] Heights(CivilDb.TinSurface s, Box d)
    {
        var z = new double[GridN * GridN];
        for (int i = 0; i < GridN; i++)
            for (int j = 0; j < GridN; j++)
            {
                double x = d.X0 + d.W * i / (GridN - 1), y = d.Y0 + d.H * j / (GridN - 1);
                try { z[i * GridN + j] = s.FindElevationAtXY(x, y); } catch { z[i * GridN + j] = double.NaN; }
            }
        return z;
    }

    /// <summary>앞 · 뒤 표본 높이 — 다른 점 수 · 가장 큰 차(면 안 ↔ 밖이 바뀌면 무한).</summary>
    static (int N, double Max, int At) HeightDiff(double[] a, double[] b)
    {
        int n = 0, at = -1; double mx = 0;
        for (int i = 0; i < a.Length; i++)
        {
            if (a[i].Equals(b[i]) || double.IsNaN(a[i])) continue;   // 앞에 면이 없던 점(다시 키워 드러남)은 잃은 것이 아님
            n++;
            double d = double.IsNaN(a[i]) || double.IsNaN(b[i]) ? double.PositiveInfinity : Math.Abs(a[i] - b[i]);
            if (d > mx || at < 0) { mx = d; at = i; }
        }
        return (n, mx, at);
    }

    static Box GeneralExt(CivilDb.TinSurface s)
    {
        try { var g = s.GetGeneralProperties(); return new Box(g.MinimumCoordinateX, g.MinimumCoordinateY, g.MaximumCoordinateX, g.MaximumCoordinateY); }
        catch { return Box.None; }
    }

    static List<(double X, double Y)> P2(IEnumerable<Point3> p) => p.Select(q => (q.X, q.Y)).ToList();
    static List<(double X, double Y)> RectList(Box b) => new() { (b.X0, b.Y0), (b.X1, b.Y0), (b.X1, b.Y1), (b.X0, b.Y1) };

    /// <summary>노선 꺾은선(0.5 m마다 + 끝 · 요소 경계) — 표본이라 곡선 바깥 불룩은 0.5 m 현의 처짐(R 10 m에서 3 mm)까지 안 봄(여유 5 m 안).</summary>
    static List<(double X, double Y)> AlignPts(CivilDb.Alignment al)
    {
        var res = new List<(double X, double Y)>();
        double s0 = al.StartingStation, s1 = al.EndingStation;
        int n = Math.Max(1, (int)Math.Ceiling((s1 - s0) / 0.5));
        for (int i = 0; i <= n; i++)
        {
            double st = i == n ? s1 : s0 + (s1 - s0) * i / n;
            double x = 0, y = 0;
            try { al.PointLocation(st, 0, ref x, ref y); res.Add((x, y)); } catch { }
        }
        return res;
    }

    /// <summary>이 노선이 땅 크기 면을 읽나(표면 종단 원천 · 표본선 묶음 «샘플» 원천) · 우리 노선인가.</summary>
    static bool ReadsGround(Transaction tr, CivilDb.Alignment al, HashSet<ObjectId> ground)
    {
        try
        {
            foreach (ObjectId pid in al.GetProfileIds())
            {
                if (tr.GetObject(pid, OpenMode.ForRead) is not CivilDb.Profile pr || pr.ProfileType != CivilDb.ProfileType.EG) continue;
                try { if (pr.GetType().GetProperty("DataSourceId")?.GetValue(pr) is ObjectId o && ground.Contains(o)) return true; } catch { }
            }
            foreach (ObjectId gid in al.GetSampleLineGroupIds())
            {
                if (tr.GetObject(gid, OpenMode.ForRead) is not CivilDb.SampleLineGroup g) continue;
                try { foreach (CivilDb.SectionSource ss in g.GetSectionSources()) if (ss.IsSampled && ground.Contains(ss.SourceId)) return true; } catch { }
            }
        }
        catch { }
        return false;
    }

    static bool IsOurAlign(Transaction tr, CivilDb.Alignment al)
    {
        if ((al.Name ?? "").StartsWith(Commands.SectionCommand.AlignBase, StringComparison.Ordinal) || string.Equals(al.Layer, Commands.SectionCommand.LayerAlign, StringComparison.OrdinalIgnoreCase)) return true;
        if (RoadBuilder.IsOurs(al) || RoadDesign.Has(tr, al.ObjectId)) return true;
        try
        {
            if (!al.ExtensionDictionary.IsNull && tr.GetObject(al.ExtensionDictionary, OpenMode.ForRead) is DBDictionary d && (d.Contains(PureWithWallsBuilder.AlignKey) || d.Contains("DH_PROFEXCAV"))) return true;
        }
        catch { }
        return false;
    }

    /// <summary>★[계획 4장] 설계 자리 항목 — 정지 구역(경계 · 건 경계) · 진입도로 면 · 옹벽 · 터파기 · 우리 노선과 땅을 읽는 노선(선 + 모든 표본선) · 설계 크기 면(보이는 세모 범위 — 1 ~ 5를 놓쳐도 덮음).</summary>
    static List<GroundCrop.Item> DesignItems(Database db, Transaction tr, List<Surf> surfs, List<string> notes)
    {
        var items = new List<GroundCrop.Item>();
        try
        {
            var regs = GradingBundleStore.TryLoadAll(db, tr, out _);
            if (regs != null)
                for (int i = 0; i < regs.Count; i++)
                {
                    var g = regs[i];
                    string lb = regs.Count > 1 ? $"계획부지 {i + 1}" : "계획부지";
                    if (g.Boundary.Count > 0) items.Add(new(lb, P2(g.Boundary), true));
                    foreach (var r in new[] { g.CutClipRing ?? g.CutFinalRing, g.FillClipRing ?? g.FillFinalRing }) if (r != null && r.Count > 0) items.Add(new(lb + " 사면", P2(r), true));
                    foreach (var rs in new[] { g.CutClipRing == null ? g.CutFinalRings : null, g.FillClipRing == null ? g.FillFinalRings : null })
                        if (rs != null) foreach (var r in rs) if (r.Count > 0) items.Add(new(lb + " 사면", P2(r), true));
                }
        }
        catch (Exception ex) { notes.Add("구역 기록 못 읽음 — " + ex.Message); }
        try
        {
            if (WallListStore.TryLoad(db, tr, out var head, out var walls, out _))
            {
                foreach (var w in walls) if (w.Poly.Count > 0) items.Add(new($"옹벽 {w.No}", P2(w.Poly), false));
                foreach (var f in head.Frozen) if (f.Poly.Count > 0) items.Add(new($"옹벽 {f.No}", P2(f.Poly), false));
            }
        }
        catch (Exception ex) { notes.Add("옹벽 기록 못 읽음 — " + ex.Message); }
        try
        {
            var ex = ExcavBundleStore.TryLoadAll(db, tr, out _);
            if (ex != null)
                for (int i = 0; i < ex.Count; i++)
                {
                    if (ex[i].Bottom.Count > 0) items.Add(new($"터파기 {i + 1}", P2(ex[i].Bottom), true));
                    if (ex[i].FinalRing is { Count: > 0 } fr) items.Add(new($"터파기 {i + 1}", P2(fr), true));
                }
        }
        catch (Exception exx) { notes.Add("터파기 기록 못 읽음 — " + exx.Message); }
        var ground = new HashSet<ObjectId>(surfs.Where(s => s.K == Kind.Ground).Select(s => s.Id));
        var cdoc = Autodesk.Civil.ApplicationServices.CivilDocument.GetCivilDocument(db);
        foreach (ObjectId aid in cdoc.GetAlignmentIds())
        {
            try
            {
                if (aid.IsErased || tr.GetObject(aid, OpenMode.ForRead) is not CivilDb.Alignment al) continue;
                if (!IsOurAlign(tr, al) && !ReadsGround(tr, al, ground)) continue;
                string an = al.Name ?? "";
                bool road = RoadDesign.Has(tr, aid);
                items.Add(new(road ? $"진입도로 {an}" : $"노선 {an}", AlignPts(al), false));
                foreach (ObjectId gid in al.GetSampleLineGroupIds())
                {
                    if (tr.GetObject(gid, OpenMode.ForRead) is not CivilDb.SampleLineGroup g) continue;
                    foreach (ObjectId slid in g.GetSampleLineIds())
                    {
                        if (tr.GetObject(slid, OpenMode.ForRead) is not CivilDb.SampleLine sl) continue;
                        var v = new List<(double X, double Y)>();
                        foreach (CivilDb.SampleLineVertex q in sl.Vertices) v.Add((q.Location.X, q.Location.Y));
                        if (v.Count > 0) items.Add(new($"횡단 표본선 {an} {sl.Name}", v, false));
                    }
                }
            }
            catch (Exception ex) { notes.Add("노선 못 읽음 — " + ex.Message); }
        }
        foreach (var s in surfs.Where(s => s.K == Kind.Design && !s.Vis.IsNone))
            items.Add(new(s.Name.StartsWith(RoadSession.SurfBase, StringComparison.Ordinal) ? $"진입도로 면 {s.Name}" : $"설계 면 {s.Name}", RectList(s.Vis), true));
        return items;
    }

    // ══════════════════════════════ 본체 ══════════════════════════════

    /// <summary>★[계획 5장 2 ~ 10] 자르기 한 번. 문서 잠금은 여기서. 예외를 안 던진다(돌려줌 = 결과).</summary>
    internal static Result Run(Document doc, Box box, ObjectId groundId)
    {
        var R = new Result();
        LastCropStats = R;
        var db = doc.Database;
        var all = System.Diagnostics.Stopwatch.StartNew();
        var ci = System.Globalization.CultureInfo.InvariantCulture;
        var notes = new List<string>();
        var surfs = new List<Surf>();
        Rec rec;
        Box need = Box.None;
        FinalSurfaceBuilder.State fin0 = FinalSurfaceBuilder.State.NoRoads;
        try
        {
            // ── ① 읽기만 — 면 · 기록 · 설계 자리 ─────────────────────────────
            using (var tr = db.TransactionManager.StartTransaction())
            {
                rec = ReadRec(db, tr);
                var cdoc = Autodesk.Civil.ApplicationServices.CivilDocument.GetCivilDocument(db);
                foreach (ObjectId sid in cdoc.GetSurfaceIds())
                {
                    if (sid.IsNull || sid.IsErased) continue;
                    if (tr.GetObject(sid, OpenMode.ForRead) is not CivilDb.Surface s) continue;
                    var u = new Surf { Id = sid, Name = s.Name ?? "", Handle = sid.Handle.ToString(), K = Classify(s), IsGround = sid == groundId };
                    u.User = u.K == Kind.Ground && !OurName(u.Name) && !u.IsGround;   // 원지반(FindGroundSurface가 고른 면)은 사람 면으로 안 셈
                    try { u.Snap = s.HasSnapshot; } catch { }
                    try { u.Ood0 = s.IsOutOfDate; } catch { }
                    if (s is CivilDb.TinSurface ts && (u.K == Kind.Ground || u.K == Kind.Design))
                    {
                        u.Tris = PureWithWallsBuilder.ReadTris(ts, out int lost);
                        if (lost > 0) notes.Add($"«{u.Name}» 세모 {lost}개 못 읽음");
                        u.Vis = GroundCrop.ExtentOf(u.Tris);
                        u.Tri0 = u.Tris.Count;
                        u.All = GeneralExt(ts);
                        try
                        {
                            var oc = s.Operations;
                            int other = 0;
                            for (int i = 0; i < oc.Count; i++) { using var op = oc[i]; if (op is CivilDb.SurfaceOperationPasteSurface ps && !ps.SurfaceId.IsNull) u.Sources.Add(ps.SurfaceId); else other++; }
                            u.OnlyPaste = other == 0 && u.Sources.Count > 0;
                        }
                        catch { }
                    }
                    else if (s is CivilDb.TinVolumeSurface tv) { try { var vp = tv.GetVolumeProperties(); u.Sources.Add(vp.BaseSurface); u.Sources.Add(vp.ComparisonSurface); } catch { } }
                    else if (s is CivilDb.GridVolumeSurface gv) { try { var vp = gv.GetVolumeProperties(); u.Sources.Add(vp.BaseSurface); u.Sources.Add(vp.ComparisonSurface); } catch { } }
                    if (u.K == Kind.Skip) R.Skipped.Add(u.Name + "(짓는중 · 임시)");
                    if (u.K == Kind.Other) R.Skipped.Add(u.Name + "(격자면 — 못 자름)");
                    surfs.Add(u);
                }
                var items = DesignItems(db, tr, surfs, notes);
                var cov = GroundCrop.Covers(box, items);
                R.Cover = cov;
                need = GroundCrop.DesignBox(items);
                try { fin0 = FinalSurfaceBuilder.StateOf(db, tr, out _, out _); } catch { }
                tr.Commit();
                if (!cov.Ok)
                {
                    R.Refused = true;
                    foreach (var o in cov.Outs) R.Red.Add(string.Format(ci, "{0} {1:F1} m", o.Label, o.Length > 0 ? o.Length : o.Far));
                    R.Why = "설계 자리가 상자 밖 — " + string.Join(" · ", R.Red.Take(8)) + (R.Red.Count > 8 ? $" 외 {R.Red.Count - 8}" : "");
                    R.Line = $"\n■ DHCROP 거절 — {R.Why} · 상자 {box.Text} · 필요 {cov.Need.Text} · {all.ElapsedMilliseconds} ms";
                    try { DiagLog.AppendCarry(R.Line + "\n"); } catch { }
                    return R;
                }
            }
            // ── ② 대상 · 딸린 면 ─────────────────────────────────────────
            var byId = surfs.ToDictionary(s => s.Id);
            foreach (var s in surfs.Where(s => s.K == Kind.Ground))
            {
                bool had = rec.Surf.TryGetValue(s.Handle, out var re) && re.Bands.Count > 0;
                bool over = GroundCrop.NeedsCrop(s.Vis, box);
                // 상자와 안 겹치는 면(보이는 세모가 다 밖) — 자르면 빈 면이 된다 → 손 안 대고 이름만(원지반이면 앞에서 이미 거절)
                bool apart = !s.Vis.IsNone && (s.Vis.X1 <= box.X0 || s.Vis.X0 >= box.X1 || s.Vis.Y1 <= box.Y0 || s.Vis.Y0 >= box.Y1);
                if (apart && !had) { R.Skipped.Add(s.Name + "(상자와 안 겹침 — 그대로)"); continue; }
                if (over || had)
                {
                    s.Target = true;
                    s.OldBands = had ? re.Bands : new List<Box>();
                    s.Ext0 = rec.Surf.TryGetValue(s.Handle, out var re2) && !re2.Ext0.IsNone ? re2.Ext0 : had ? s.All.Union(s.Vis) : s.Vis;
                }
                else { R.Untouched++; }
                if (s.User) R.Users++;
            }
            // ★[V87] 정의가 붙여넣기 줄뿐인 스냅샷 없는 면(사람이 원지반을 붙여넣어 만든 면 등)은 띠를 넣지 않는다 — 소스가 다 잘리면 다시 짓기만으로 똑같이 작아지고(소스를 따름 · 더 큰 상자로 다시 자르면 저절로 커짐 · 정의를 안 바꿈).
            //   ⚠실행 취소: 이런 면은 띠를 넣든 안 넣든 U 뒤에 세모가 잘린 채 «최신»으로 남았다(화면 없는 Civil · 현황붙임 7,731 — 원래 249,841 · 정의는 돌아옴) — 사람이 [재작성]하면 돌아온다. 소스가 하나라도 안 잘리면 종전대로 띠.
            for (bool more = true; more;)
            {
                more = false;
                foreach (var s in surfs.Where(s => s.Target && s.OnlyPaste && !s.Snap && s.OldBands.Count == 0))
                    if (s.Sources.All(id => byId.TryGetValue(id, out var q) && (q.Target || q.Dep) && q.Id != s.Id)) { s.Target = false; s.Dep = true; more = true; }
            }
            // 딸린 면 — 대상(또는 딸린 면)을 붙여넣은 스냅샷 없는 면 · 체적면(그 소스가 바뀌면 구식으로 남는다 — V86 «(옛)»)
            for (bool more = true; more;)
            {
                more = false;
                foreach (var s in surfs.Where(s => !s.Target && !s.Dep && (s.K == Kind.Volume || (!s.Snap && s.Tris != null))))
                    if (s.Sources.Any(id => byId.TryGetValue(id, out var q) && (q.Target || q.Dep))) { s.Dep = true; more = true; }
            }
            var work = surfs.Where(s => s.Target || s.Dep).ToList();
            R.Targets = work.Count(s => s.Target); R.Dependents = work.Count(s => s.Dep);
            if (R.Targets == 0)
            {
                R.Ok = true; R.Nothing = true;
                R.Done = "자를 면 없음 — 모두 상자 안";
                R.Line = $"\n■ DHCROP v2 — 상자 {box.Text} · 설계 자리 D(+5 m) 통과 · {R.Done} · 손 안 댐 {R.Untouched} · 사람 면 {R.Users} · {all.ElapsedMilliseconds} ms";
                try { DiagLog.AppendCarry(R.Line + "\n"); } catch { }
                return R;
            }
            // 소스 먼저(붙여넣기 줄 차례) · 같으면 스냅샷 없는 면 먼저(V84 차례) · 체적면 끝
            var order = new List<Surf>();
            var seen = new HashSet<ObjectId>();
            void Visit(Surf s)
            {
                if (!seen.Add(s.Id)) return;
                foreach (var id in s.Sources) if (byId.TryGetValue(id, out var q) && (q.Target || q.Dep)) Visit(q);
                order.Add(s);
            }
            foreach (var s in work.OrderBy(s => s.K == Kind.Volume ? 2 : s.Snap ? 1 : 0).ThenBy(s => s.Id.Handle.Value)) Visit(s);
            R.Chain = order.Count(s => s.Sources.Count > 0 && (s.K == Kind.Volume || !s.Snap));
            // 앞 상태 — 안쪽 지문 · D 안 표본 높이(D 없으면 상자 − 5 m)
            var dBox = need.IsNone ? box.Grow(-GroundCrop.Margin) : need;
            using (var tr = db.TransactionManager.StartTransaction())
            {
                foreach (var s in work.Where(s => s.Tris != null))
                {
                    s.HashBox = s.OldBands.Count > 0 && !rec.Box.IsNone ? box.Intersect(rec.Box) : box;
                    s.Hash0 = GroundCrop.InsideHash(s.Tris!, s.HashBox);
                    s.Heights0 = Heights((CivilDb.TinSurface)tr.GetObject(s.Id, OpenMode.ForRead), dBox);
                }
                tr.Commit();
            }
            // ── ③ 넣기 · 재작성 · 확인 — 바깥 트랜잭션(어긋나면 통째로 물림 · V86 (vi′)) ───────────
            string fail = "";
            var sw = System.Diagnostics.Stopwatch.StartNew();
            using (doc.LockDocument())
            using (var outer = db.TransactionManager.StartTransaction())
            {
                try
                {
                    using (var t1 = db.TransactionManager.StartTransaction())
                    {
                        foreach (var s in work.Where(s => s.Target))
                        {
                            var ts = (CivilDb.TinSurface)t1.GetObject(s.Id, OpenMode.ForWrite);
                            var bl = Bounds(ts);
                            var ours = GroundCrop.OurBoundaries(bl.Select(b => b.B).ToList(), s.OldBands.Count > 0 ? s.OldBands : null, s.IsGround);
                            var bd = ts.BoundariesDefinition;
                            foreach (var (ix, _) in ours.OrderByDescending(o => o.Index)) { bd.RemoveAt(bl[ix].Op); s.Removed++; }
                            var e = s.Vis.Union(s.All).Union(s.Ext0);
                            s.NewBands = GroundCrop.Frame(box, e);
                            if (SkipBandsForTest && ReferenceEquals(s, work.First(q => q.Target))) continue;
                            foreach (var b in s.NewBands) bd.AddBoundaries(RectPts(b), 0.1, Autodesk.Civil.SurfaceBoundaryType.Hide, true);
                        }
                        t1.Commit();
                    }
                    using (var t2 = db.TransactionManager.StartTransaction())
                    {
                        foreach (var s in order)
                        {
                            var w = (CivilDb.Surface)t2.GetObject(s.Id, OpenMode.ForWrite);
                            var rw = System.Diagnostics.Stopwatch.StartNew();
                            try
                            {
                                if (FailRebuildForTest && s.Target && ReferenceEquals(s, order.First(q => q.Target))) throw new InvalidOperationException("시험 고리 — 재작성 실패");
                                w.Rebuild(); s.Rebuilt = "OK";
                            }
                            catch (Exception rex) { s.Rebuilt = rex.GetType().Name + ": " + rex.Message; }
                            s.RebuildMs = rw.ElapsedMilliseconds;
                        }
                        t2.Commit();
                    }
                    // 확인 — 마지막 쓰기 뒤 새 트랜잭션(안쪽 지문 · 표본 높이 · 세모 > 0 · 구식 그대로)
                    var bad = new List<string>();
                    using (var t3 = db.TransactionManager.StartTransaction())
                    {
                        foreach (var s in work)
                        {
                            var w = (CivilDb.Surface)t3.GetObject(s.Id, OpenMode.ForRead);
                            bool ood = true; try { ood = w.IsOutOfDate; } catch { }
                            if (ood && !s.Ood0) { bad.Add($"«{s.Name}» 재작성 뒤 구식({s.Rebuilt})"); R.Stale++; }
                            // ★[v111.9 · 설치 전 검토 H1] 재작성이 던졌으면 — 앞부터 구식이던 면도(지문 · 세모 수만으로는 못 가름) — 실패
                            if (s.Rebuilt != "OK") bad.Add($"«{s.Name}» 재작성 실패({s.Rebuilt})");
                            if (w is not CivilDb.TinSurface ts) continue;
                            var t = PureWithWallsBuilder.ReadTris(ts, out _);
                            s.Tri1 = t.Count;
                            R.Checked++;
                            if (s.Target && t.Count == 0) { bad.Add($"«{s.Name}» 상자 안에 세모 없음"); continue; }
                            // ★[v111.9 · 설치 전 검토 H1] 대상 면은 정말 상자 밖 세모를 잃었나 — 무게중심이 상자(+ 되읽기 끝 1 µm) 밖인 세모 0개여야(띠가 안 먹었으면 여기서 잡힘)
                            if (s.Target)
                            {
                                var okBox = box.Grow(GroundCrop.VertexTol);
                                int outN = t.Count(q => !okBox.Contains((q.A.X + q.B.X + q.C.X) / 3, (q.A.Y + q.B.Y + q.C.Y) / 3));
                                if (outN > 0) bad.Add($"«{s.Name}» 상자 밖 세모 {outN}개가 남음(띠가 안 먹음)");
                            }
                            string h = GroundCrop.InsideHash(t, s.HashBox);
                            if (h == s.Hash0) R.SameInside++; else bad.Add($"«{s.Name}» 안쪽 세모가 바뀜({s.Hash0} → {h})");
                            var hd = HeightDiff(s.Heights0, Heights(ts, dBox));
                            R.HeightBits += hd.N; R.HeightMax = Math.Max(R.HeightMax, hd.Max);
                            if (hd.Max > HeightEps) { R.HeightDiff++; bad.Add(string.Format(ci, "«{0}» 설계 자리 표본 높이가 바뀜({1}/{2}점 · 최대 {3:E2} m · 점 {4})", s.Name, hd.N, GridN * GridN, hd.Max, hd.At)); }
                        }
                        t3.Commit();
                    }
                    if (FailVerifyForTest) bad.Add("시험 고리 — 확인 실패");
                    if (bad.Count > 0) fail = string.Join(" · ", bad);
                    else WriteRec(db, outer, box, surfs.Where(s => s.Target && s.NewBands.Count > 0));   // 띠가 든 면은 늘 대상이라(②) 대상만 적으면 기록이 빠지지 않는다
                }
                catch (Exception ex) { fail = "예외 " + ex.GetType().Name + ": " + ex.Message; }
                if (fail.Length > 0) outer.Abort();
                else outer.Commit();
            }
            long msCrop = sw.ElapsedMilliseconds;
            string per = string.Join(" · ", order.Select(s => s.Tris == null ? $"{s.Name}(체적면 {s.Rebuilt} {s.RebuildMs} ms)"
                                                       : string.Format(ci, "{0} {1:N0}→{2:N0}({3:F2} s{4}{5})", s.Name, s.Tri0, s.Tri1, s.RebuildMs / 1000.0, s.Dep ? " · 딸린 면" : $" · 띠 {s.NewBands.Count} · 뺌 {s.Removed}", s.Rebuilt == "OK" ? "" : " · " + s.Rebuilt)));
            if (fail.Length > 0)
            {
                R.RolledBack = true;
                R.Why = fail;
                R.Line = $"\n■ DHCROP 되돌림 — {fail} · 상자 {box.Text} · 면 {work.Count}: {per} · 넣기~확인 {msCrop} ms(바깥 트랜잭션 물림 — 모든 면 그대로)";
                try { DiagLog.AppendCarry(R.Line + "\n"); } catch { }
                return R;
            }
            // ── ④ 커밋 뒤 되읽기(화면을 정하는 값 — 새 트랜잭션) ─────────────────────
            using (var tr = db.TransactionManager.StartTransaction())
            {
                foreach (var s in work)
                    try { if (((CivilDb.Surface)tr.GetObject(s.Id, OpenMode.ForRead)).IsOutOfDate && !s.Ood0) { R.Stale++; notes.Add($"⚠«{s.Name}» 커밋 뒤 구식"); } } catch { }
                tr.Commit();
            }
            // ── ⑤ 최종지표면_DH — 자르기 전 «맞음»이었으면 같은 명령에서 다시 지음(자른 정지면_DH 위 · 도장 «맞음») ─────
            if (fin0 == FinalSurfaceBuilder.State.Fresh)
            {
                var fw = System.Diagnostics.Stopwatch.StartNew();
                var ses = RoadSession.Of(doc);
                if (ses != null) R.FinalNote = ses.MakeFinal();
                else
                {
                    var evPrev = EventViewerMute.Begin();
                    try { R.FinalNote = FinalSurfaceBuilder.Build(doc); }
                    finally { EventViewerMute.End(evPrev); }
                }
                R.FinalMs = fw.ElapsedMilliseconds;
            }
            // ── ⑥ 다시 키운 상자 — 못 커진 면(Q3) ─────────────────────────────
            if (!rec.Box.IsNone)
            {
                var g = GroundCrop.Grew(rec.Box, box);
                if (g.L || g.R || g.B || g.T)
                    using (var tr = db.TransactionManager.StartTransaction())
                    {
                        foreach (var s in work.Where(s => s.Target && !s.IsGround))
                        {
                            if (s.Id.IsErased || tr.GetObject(s.Id, OpenMode.ForRead) is not CivilDb.TinSurface ts) continue;
                            if (GroundCrop.NotGrown(rec.Box, box, s.Ext0, GroundCrop.ExtentOf(PureWithWallsBuilder.ReadTris(ts, out _)))) R.NotGrown.Add(s.Name);
                        }
                        tr.Commit();
                    }
                if (R.NotGrown.Count > 0) R.GrowNote = $"DH 면은 [정지면 생성]을 다시 하면 넓어집니다({string.Join(" · ", R.NotGrown)})";
            }
            R.Ok = true;
            R.Ms = all.ElapsedMilliseconds;
            int tgt = work.Count(s => s.Target);
            R.Done = $"면 {tgt}개 자름" + (R.Chain > 0 ? $" · 붙여넣기 · 체적면 {R.Chain}개는 다음 명령(DHCROPDEPS)에서 다시 지음" : "")
                   + (R.FinalNote.Length > 0 ? (R.FinalNote.StartsWith("⚠") ? " · ⚠최종지표면 못 다시 지음 — 진입도로 창 [업데이트]" : " · 최종지표면 다시 지음") : "");
            R.Line = string.Format(ci, "\n■ DHCROP v2 — 상자 {0} · 설계 자리 D(+5 m) {1} · 면 {2}: {3} · 손 안 댐 {4}(상자 안) · 사람 면 {5} · 안쪽 지문 같음 {6}/{7} · 표본 높이 바뀐 면 {8}(끝자리만 다른 점 {16} · 최대 {17:E1} m) · 구식 {9}{10}{11}{12}{13} · 넣기~확인 {14} ms · 합 {15:F1} s",
                box.Text, need.IsNone ? "없음" : "통과", work.Count, per, R.Untouched, R.Users, R.SameInside, R.Checked, R.HeightDiff, R.Stale,
                R.FinalNote.Length > 0 ? $" · 최종면 «{R.FinalNote}» {R.FinalMs} ms" : fin0 == FinalSurfaceBuilder.State.NoRoads ? "" : $" · 최종면 {fin0}(다시 안 지음)",
                R.NotGrown.Count > 0 ? " · 못 커진 면 [" + string.Join(" · ", R.NotGrown) + "]" : "",
                R.Skipped.Count > 0 ? " · 건너뜀 [" + string.Join(" · ", R.Skipped) + "]" : "",
                notes.Count > 0 ? " · " + string.Join(" · ", notes) : "", msCrop, R.Ms / 1000.0, R.HeightBits, R.HeightMax);
            try { DiagLog.AppendCarry(R.Line + "\n"); } catch { }
            return R;
        }
        catch (Exception ex)
        {
            R.Why = "예외 " + ex.GetType().Name + ": " + ex.Message;
            R.Line = $"\n■ DHCROP 실패 — {R.Why}";
            try { DiagLog.AppendCarry(R.Line + "\n"); } catch { }
            return R;
        }
    }

    /// <summary>★[DHCROPDEPS] 씨앗(원지반 · 기록에 든 자른 면)을 (사슬로) 붙여넣은 <b>스냅샷 없는</b> 면 · 체적면을 소스 차례로 하나씩 제 트랜잭션에서 다시 짓는다(씨앗 자신이 그런 면이어도).
    /// <para>소스를 다시 지은 명령 안에서 지으면 명령 끝에 «구식»이 붙는다(V87 final · undo_b 실측) → 소스를 안 짓는 <b>뒤 명령</b>에서 부른다 · 깃발은 아직 안 보일 수 있어 안 보고 다 짓는다(같은 소스 · 같은 세모).
    /// 안 하면 구식으로 남고 실행 취소로도 안 풀린다(V86 «(옛)»). 돌려줌 = 한 줄(빈 글 = 한 일 없음).</para></summary>
    internal static string RebuildDependents(Database db, ICollection<ObjectId> seeds, out List<ObjectId> builtIds)
    {
        var done = new List<string>();
        builtIds = new List<ObjectId>();
        try
        {
            var cdoc = Autodesk.Civil.ApplicationServices.CivilDocument.GetCivilDocument(db);
            var cand = new List<(ObjectId Id, List<ObjectId> S)>();
            using (var tr = db.TransactionManager.StartTransaction())
            {
                foreach (ObjectId sid in cdoc.GetSurfaceIds())
                {
                    try
                    {
                        if (sid.IsErased || tr.GetObject(sid, OpenMode.ForRead) is not CivilDb.Surface s) continue;
                        var l = new List<ObjectId>();
                        if (s is CivilDb.TinSurface && !s.HasSnapshot)
                        {
                            var oc = s.Operations;
                            for (int i = 0; i < oc.Count; i++) { using var op = oc[i]; if (op is CivilDb.SurfaceOperationPasteSurface ps && !ps.SurfaceId.IsNull) l.Add(ps.SurfaceId); }
                        }
                        else if (s is CivilDb.TinVolumeSurface tv) { var vp = tv.GetVolumeProperties(); l.Add(vp.BaseSurface); l.Add(vp.ComparisonSurface); }
                        if (l.Count > 0) cand.Add((sid, l));
                    }
                    catch { }
                }
                tr.Commit();
            }
            // 소스 차례(원지반에서 닿는 것만) — 하나씩 제 트랜잭션
            var src = new HashSet<ObjectId>(seeds);
            var built = new HashSet<ObjectId>();
            for (bool more = true; more;)
            {
                more = false;
                foreach (var (id, l) in cand)
                {
                    if (built.Contains(id) || !l.Any(src.Contains)) continue;
                    built.Add(id); src.Add(id); more = true;
                    string nm = "?";
                    try
                    {
                        using var trR = db.TransactionManager.StartTransaction();
                        var w = (CivilDb.Surface)trR.GetObject(id, OpenMode.ForWrite);
                        nm = w.Name;
                        w.Rebuild();
                        trR.Commit();
                        done.Add(nm); builtIds.Add(id);
                    }
                    catch (Exception ex) { done.Add($"{nm}(⚠{ex.Message})"); }
                }
            }
        }
        catch (Exception ex) { done.Add("⚠" + ex.Message); }
        return done.Count == 0 ? "" : "딸린 면 다시 지음 " + string.Join(" · ", done);
    }
}
