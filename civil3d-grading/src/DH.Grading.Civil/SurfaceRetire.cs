using Autodesk.AutoCAD.DatabaseServices;
using CivilDb = Autodesk.Civil.DatabaseServices;

namespace DH.Grading.Civil;

/// <summary>★★★[v111.10 · 계획_초기화.md 3.1 «물러남 규칙» · 1013 메모 «붙여넣기 소스가 지워진 면의 재작성 → Civil이 죽음»] 우리 면을 지울 때 남의 면이 그 면을 쓰면(붙여넣기 · 체적면 기준/비교)
/// 지우지 않고 «물러나게» 한다 — 이름 «…(옛)» · 표식 <see cref="Key"/> · 제 레이어 <see cref="Layer"/> · 설명 · 숨김(넷 한 번에).
/// <para>세 초기화(전체 · 계획지표면 · 진입도로) · 원지반 삭제 · 좌표계 변경 · 최종지표면 다시 짓기(<see cref="FinalSurfaceBuilder.RetireOld(Database, Transaction, ObjectId, out int)"/>)가 같은 규칙을 쓴다.</para>
/// <para>알아보기는 이름 꼴이 아니라 <b>표식</b>으로(<see cref="IsRetired(CivilDb.Surface)"/>) — 이 규칙이 만드는 «가상터파기_DH3(옛)» 같은 이름은 이름 꼴로 다 못 잡고, 사람이 이름을 또 바꿀 수도 있다.
/// 표식 없는 v111.8 옛 최종면(«최종지표면_DH(옛)»)만 이름으로도.</para></summary>
internal static class SurfaceRetire
{
    /// <summary>물러난 면의 레이어 — «DH-» · «DH도로-»로 시작하지 않아 선 지우기 훑기 · 남은 것 셈(«DH» 머리)에 안 걸린다.</summary>
    internal const string Layer = "옛면_DH";
    /// <summary>면 확장 사전 XRecord — [0] 원래 이름 [1] 시각 · 판 [2] 쓰는 면 이름 [3] 이 면 핸들(복사한 면 가르기).</summary>
    internal const string Key = "DH_RETIRED";
    /// <summary>설명 머리 — «DH 진입도로 지표면»으로 시작하지 않게(<see cref="FinalSurfaceBuilder.IsRoadSurface"/> 거짓).</summary>
    internal const string DescHead = "DH 물러난 면";

    /// <summary>물러난 면인가(표식 · 또는 v111.8 옛 최종면 이름) — 트랜잭션 없이 불러도 됨(열고 닫는 트랜잭션으로 읽음).</summary>
    internal static bool IsRetired(CivilDb.Surface s)
    {
        try
        {
            if (FinalSurfaceBuilder.IsOldName(s.Name ?? "")) return true;
            if (s.ExtensionDictionary.IsNull) return false;
            // ★[1103 화면 없는 Civil 실측] 바깥 트랜잭션이 있으면 그것으로 읽는다 — 열고 닫는 트랜잭션을 물리면(Dispose) 같은 사전에 바깥 트랜잭션이 쓴 표식이 커밋 뒤 사라졌다
            var top = s.Database.TransactionManager.TopTransaction;
            if (top != null) return Stamped(top, s);
            using var tr = s.Database.TransactionManager.StartOpenCloseTransaction();
            bool r = Stamped(tr, s);
            tr.Commit();
            return r;
        }
        catch { return false; }
    }

    /// <summary>★[v111.10 · 설치 전 검토 1103 · 낮음] 물러나기 전 이름(표식 [0]) — 표식 없는 v111.8 옛 최종면은 «최종지표면_DH». 모르면 빈 글.</summary>
    internal static string OriginalName(Transaction tr, CivilDb.Surface s)
    {
        try
        {
            if (!s.ExtensionDictionary.IsNull && tr.GetObject(s.ExtensionDictionary, OpenMode.ForRead) is DBDictionary d && d.Contains(Key)
                && tr.GetObject(d.GetAt(Key), OpenMode.ForRead) is Xrecord xr && xr.Data != null)
            {
                var a = xr.Data.AsArray();
                if (a.Length > 0) return a[0].Value as string ?? "";
            }
            if (FinalSurfaceBuilder.IsOldName(s.Name ?? "")) return FinalSurfaceBuilder.Name;
        }
        catch { }
        return "";
    }

    /// <summary>표식이 있고 그 표식이 이 면 것인가 — [3] 핸들이 다르면 사람이 복사한 면(확장 사전이 따라옴 — 남의 면으로 봄).</summary>
    static bool Stamped(Transaction tr, CivilDb.Surface s)
    {
        if (tr.GetObject(s.ExtensionDictionary, OpenMode.ForRead) is not DBDictionary d || !d.Contains(Key)) return false;
        if (tr.GetObject(d.GetAt(Key), OpenMode.ForRead) is not Xrecord xr || xr.Data == null) return true;
        var a = xr.Data.AsArray();
        return a.Length < 4 || (a[3].Value as string ?? "") == s.Handle.ToString();
    }

    /// <summary>★[v111.10 · 계획 10장 Q2 · 검토 1103] 면 말고 우리 면을 쓰는 <b>사람 것</b> — 표본선 묶음(단면 원천) · 관망(기준면) · 코리더(대상) · Civil 그레이딩. 그 면은 지우지 않고 물러남
    /// (원천이 지워진 묶음은 읽을 때 Civil이 죽을 수 있다 — 1013 길 · 안 잼). 우리 노선(진입도로 · «DH선형») · 진입도로 기록이 가리키는 묶음 · 우리 레이어의 묶음은 뺀다(우리 것은 지금처럼).
    /// <para>★[설치 전 검토 1103 · 중간] 못 읽으면 지울 면 <b>모두</b> «쓴다»(SourcesOf와 같은 법 — 삼키면 그 면이 지워져 1103 꼴) · 그레이딩은 대상 면을 읽을 API가 없어 도면에 하나라도 있으면 모두 «쓴다».
    /// 데이터 바로가기(다른 도면이 이 면을 참조)는 이 도면에서 알 길이 없다(안 봄).</para>
    /// 돌려줌 = 면 → 쓰는 것 이름들.</summary>
    internal static Dictionary<ObjectId, string> HumanKeep(Database db, Transaction tr, ICollection<ObjectId> victims)
    {
        var keep = new Dictionary<ObjectId, string>();
        if (victims.Count == 0) return keep;
        var V = victims as HashSet<ObjectId> ?? new HashSet<ObjectId>(victims);
        void Keep(ObjectId sid, string who) { if (keep.TryGetValue(sid, out var w)) { if (!w.Contains(who)) keep[sid] = w + " · " + who; } else keep[sid] = who; }
        void All(string who) { foreach (var v in V) Keep(v, who); }
        try
        {
            var civ = Autodesk.Civil.ApplicationServices.CivilDocument.GetCivilDocument(db);
            foreach (ObjectId aid in civ.GetAlignmentIds())
            {
                if (aid.IsNull || aid.IsErased) continue;
                try
                {
                    if (tr.GetObject(aid, OpenMode.ForRead) is not CivilDb.Alignment al) continue;
                    if (RoadBuilder.IsOurs(al) || (al.Name ?? "").StartsWith(Commands.SectionCommand.AlignBase, StringComparison.OrdinalIgnoreCase)) continue;
                    var mine = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                    var d = RoadDesign.Read(tr, aid);
                    if (d != null) { mine.Add(d.SheetGroup); mine.Add(d.XsecGroup); }
                    foreach (ObjectId gid in al.GetSampleLineGroupIds())
                    {
                        if (gid.IsNull || gid.IsErased || mine.Contains(gid.Handle.ToString())) continue;
                        string gn = gid.Handle.ToString();
                        try
                        {
                            if (tr.GetObject(gid, OpenMode.ForRead) is not CivilDb.SampleLineGroup g || (g.Layer ?? "").StartsWith(RoadBuilder.AlignBase + "-", StringComparison.Ordinal)) continue;
                            gn = g.Name ?? gn;
                            // ★단면에서 원천을 읽음 — GetSectionSources()는 1103 화면 없는 Civil에서 탐침이 만든 묶음을 읽다 죽었다(초기화 없이도)
                            foreach (ObjectId slid in g.GetSampleLineIds())
                            {
                                if (slid.IsNull || slid.IsErased || tr.GetObject(slid, OpenMode.ForRead) is not CivilDb.SampleLine sl) continue;
                                foreach (ObjectId secId in sl.GetSectionIds())
                                    if (!secId.IsNull && !secId.IsErased && tr.GetObject(secId, OpenMode.ForRead) is CivilDb.Section sec && V.Contains(sec.SourceId)) Keep(sec.SourceId, $"표본선 묶음 «{gn}»");
                            }
                        }
                        catch { All($"표본선 묶음 «{gn}»(못 읽음)"); }
                    }
                }
                catch { All($"노선 {aid.Handle}(못 읽음)"); }
            }
            foreach (ObjectId nid in civ.GetPipeNetworkIds())
            {
                if (nid.IsNull || nid.IsErased) continue;
                try { if (tr.GetObject(nid, OpenMode.ForRead) is CivilDb.Network n && V.Contains(n.ReferenceSurfaceId)) Keep(n.ReferenceSurfaceId, $"관망 «{n.Name}»"); }
                catch { All($"관망 {nid.Handle}(못 읽음)"); }
            }
            // 코리더 — 대상(SubassemblyTargetInfo.TargetIds)에 우리 면 · 못 읽으면 모두
            foreach (ObjectId cid in civ.CorridorCollection)
            {
                if (cid.IsNull || cid.IsErased) continue;
                string cn = cid.Handle.ToString();
                try
                {
                    if (tr.GetObject(cid, OpenMode.ForRead) is not CivilDb.Corridor c) continue;
                    cn = c.Name ?? cn;
                    foreach (CivilDb.SubassemblyTargetInfo t in c.GetTargets())
                        foreach (ObjectId x in t.TargetIds) if (V.Contains(x)) Keep(x, $"코리더 «{cn}»");
                }
                catch { All($"코리더 «{cn}»(못 읽음)"); }
            }
            // Civil 그레이딩 — 대상 면을 읽을 관리형 API가 없음(AeccDbGrading · GradingGroup) → 하나라도 있으면 모두(모르는 것 = 쓴다)
            var ms = (BlockTableRecord)tr.GetObject(SymbolUtilityServices.GetBlockModelSpaceId(db), OpenMode.ForRead);
            foreach (ObjectId id in ms)
            {
                string cls = ""; try { cls = id.ObjectClass?.Name ?? ""; } catch { }
                if (cls.IndexOf("Grading", StringComparison.OrdinalIgnoreCase) >= 0) { All("Civil 그레이딩(대상 면을 못 읽음)"); break; }
            }
        }
        catch { All("사람 것(못 읽음)"); }
        return keep;
    }

    /// <summary>한 면이 쓰는 면(붙여넣기 · 체적면 기준/비교) — 정의를 못 읽으면 거짓.</summary>
    static bool SourcesOf(Transaction tr, ObjectId id, List<ObjectId> into)
    {
        try
        {
            if (tr.GetObject(id, OpenMode.ForRead) is not CivilDb.Surface s) return true;
            CivilDb.VolumeSurfaceProperties? vp = s is CivilDb.TinVolumeSurface tv ? tv.GetVolumeProperties() : s is CivilDb.GridVolumeSurface gv ? gv.GetVolumeProperties() : null;
            if (vp != null) { into.Add(vp.BaseSurface); into.Add(vp.ComparisonSurface); return true; }
            var oc = s.Operations;
            for (int i = 0; i < oc.Count; i++)
            {
                using var op = oc[i];
                if (op is CivilDb.SurfaceOperationPasteSurface ps && !ps.SurfaceId.IsNull) into.Add(ps.SurfaceId);
            }
            return true;
        }
        catch { return false; }
    }

    static string NameOf(Transaction tr, ObjectId id)
    {
        try { return tr.GetObject(id, OpenMode.ForRead) is CivilDb.Surface s ? s.Name ?? "?" : "?"; } catch { return "?"; }
    }

    /// <summary>읽기만 — 지울 면 목록 <paramref name="victims"/> 가운데 남아야 하는 면 P(남는 면이 쓰는 면 + 그 소스를 따라간 닫힌 묶음) → 그 면을 쓰는 것(이름).
    /// 남는 면(목록 밖)마다 정의를 한 번 읽는다 · 못 읽은 면은 «다 쓴다»(지우면 그 면이 매달린 소스를 물 수 있음 — 보수적).</summary>
    /// <remarks>면 말고 그 면을 쓰는 사람 표본선 묶음 · 관망(<see cref="HumanKeep"/>)도 «쓴다»로 — 부르는 곳 모두(초기화 · 원지반 삭제 · 최종면 다시 짓기).</remarks>
    internal static Dictionary<ObjectId, string> MustStay(Database db, Transaction tr, ICollection<ObjectId> victims)
    {
        var V = new HashSet<ObjectId>(victims);
        var P = new Dictionary<ObjectId, string>();
        if (V.Count == 0) return P;
        var seed = HumanKeep(db, tr, V);
        var cdoc = Autodesk.Civil.ApplicationServices.CivilDocument.GetCivilDocument(db);
        var users = new Dictionary<ObjectId, List<string>>();
        void Use(ObjectId v, string who) { if (!users.TryGetValue(v, out var l)) users[v] = l = new(); if (!l.Contains(who)) l.Add(who); }
        foreach (var kv in seed) foreach (var w in kv.Value.Split(" · ")) Use(kv.Key, w);
        foreach (ObjectId sid in cdoc.GetSurfaceIds())
        {
            if (sid.IsNull || sid.IsErased || V.Contains(sid)) continue;
            var src = new List<ObjectId>();
            string nm = NameOf(tr, sid);
            if (!SourcesOf(tr, sid, src)) { foreach (var v in V) Use(v, nm + "(못 읽음)"); continue; }
            foreach (var x in src) if (V.Contains(x)) Use(x, nm);
        }
        var q = new Queue<ObjectId>(users.Keys);
        foreach (var kv in users) P[kv.Key] = string.Join(" · ", kv.Value);
        while (q.Count > 0)
        {
            var u = q.Dequeue();
            var src = new List<ObjectId>();
            bool ok = SourcesOf(tr, u, src);
            string un = NameOf(tr, u);
            foreach (var x in ok ? src : V.ToList())
            {
                if (!V.Contains(x) || P.ContainsKey(x)) continue;
                P[x] = $"«{un}»의 소스" + (ok ? "" : "(못 읽음)");
                q.Enqueue(x);
            }
        }
        return P;
    }

    /// <summary>한 번 치운 결과.</summary>
    internal sealed class Result
    {
        public int Erased;
        public readonly List<string> ErasedNames = new();
        /// <summary>이번에 물러난 면 «원래 이름 → 새 이름(«쓰는 면»이 씀)».</summary>
        public readonly List<string> Retired = new();
        /// <summary>이미 물러나 있었고 아직 남이 써서 그대로 둔 면.</summary>
        public readonly List<string> StillUsed = new();
        public readonly HashSet<ObjectId> Kept = new();
        public readonly List<string> Failed = new();
        /// <summary>한 줄(로그 · 명령창) — 빈 글 = 한 일 없음.</summary>
        public string Text => string.Join(" · ", new[]
        {
            Erased > 0 ? $"지표면 {Erased}개 지움" : "",
            Retired.Count > 0 ? "⚠남이 쓰는 면은 이름만 바꿔 숨김 — " + string.Join(" · ", Retired) : "",
            StillUsed.Count > 0 ? "물러난 면 남김(아직 씀) — " + string.Join(" · ", StillUsed) : "",
            Failed.Count > 0 ? "⚠못 치움 — " + string.Join(" · ", Failed) : "",
        }.Where(t => t.Length > 0));
    }

    /// <summary>★[3.1] 지울 면 목록을 치운다 — 남이 쓰는 면(+ 그 소스)은 물러남 · 나머지는 지움(쓰는 면부터 — 살아 있는 면이 지워진 소스를 무는 때가 없게).
    /// 이미 물러난 면은 그대로(이름을 또 안 바꿈). 문서 · 트랜잭션은 부르는 쪽.</summary>
    /// <param name="stay">이미 잰 <see cref="MustStay"/>(null = 여기서 잼) — 부르는 쪽이 지우기 앞에 따로 읽었으면 그것을 넘김.</param>
    internal static Result Apply(Database db, Transaction tr, IEnumerable<ObjectId> victims, Dictionary<ObjectId, string>? stay = null)
    {
        var R = new Result();
        var V = victims.Where(id => !id.IsNull && !id.IsErased).Distinct().ToList();
        if (V.Count == 0) return R;
        var P = stay ?? MustStay(db, tr, V);
        // ① 지움 — 쓰는 면부터(지울 묶음 안에서 아무도 안 쓰게 된 면을 차례로)
        var E = V.Where(id => !P.ContainsKey(id)).ToList();
        var srcOf = new Dictionary<ObjectId, List<ObjectId>>();
        foreach (var id in E) { var l = new List<ObjectId>(); SourcesOf(tr, id, l); srcOf[id] = l.Where(E.Contains).ToList(); }
        var left = new List<ObjectId>(E);
        while (left.Count > 0)
        {
            var free = left.Where(x => !left.Any(y => y != x && srcOf[y].Contains(x))).ToList();
            if (free.Count == 0) free = new List<ObjectId> { left[0] };   // 서로 쓰는 고리(있을 수 없음) — 차례대로
            foreach (var id in free)
            {
                if (!left.Remove(id)) continue;
                string nm = NameOf(tr, id);
                try { tr.GetObject(id, OpenMode.ForWrite, false, true).Erase(); R.Erased++; R.ErasedNames.Add(nm); }
                catch (Exception ex)
                {
                    // ★[검토 1103] 못 지운 면이 남으면 그 면의 소스도 지우면 안 됨(지워진 소스를 무는 면 = 1013 길) → 소스는 물러남으로
                    R.Failed.Add($"{nm} 지우기 {ex.Message}"); R.Kept.Add(id);
                    var q = new Queue<ObjectId>(srcOf[id]);
                    while (q.Count > 0) { var x = q.Dequeue(); if (left.Remove(x)) { P[x] = $"«{nm}»(못 지움)의 소스"; foreach (var y in srcOf[x]) q.Enqueue(y); } }
                }
            }
        }
        // ② 물러남 — 지운 뒤에 이름을 고름(지운 면의 이름이 비어 «(옛)»이 짧게)
        ObjectId lay = ObjectId.Null;
        foreach (var (id, who) in P)
        {
            R.Kept.Add(id);
            try
            {
                // ★[검토 1103] 잠긴 레이어여도 연다(지우기와 같은 법 — 못 열면 우리 이름으로 남아 다른 길이 확인 없이 지울 수 있음)
                var s = (CivilDb.Surface)tr.GetObject(id, OpenMode.ForWrite, false, true);
                if (IsRetired(s)) { R.StillUsed.Add($"{s.Name}(«{who}»가 씀)"); if (s.Visible) s.Visible = false; continue; }
                string was = s.Name ?? "";
                string stem = System.Text.RegularExpressions.Regex.Replace(was, @"_\d+$", "");
                string nm = UniqueName(db, tr, stem + "(옛)");
                // 표식 먼저(이름만 바뀌고 표식이 없는 면이 생기지 않게) · [3] = 이 면 핸들(복사한 면은 물러난 면이 아님)
                WriteStamp(tr, s, was, $"{DateTime.Now:yyyy-MM-dd HH:mm:ss} · {GradingSettings.Version}", who, s.Handle.ToString());
                s.Name = nm;
                if (lay.IsNull) lay = Commands.SectionCommand.EnsureLayer(db, tr, Layer, 8);
                try { s.LayerId = lay; } catch { }
                try { s.Description = $"{DescHead} — {was} · {DateTime.Now:yyyy-MM-dd}"; } catch { }
                if (s.Visible) s.Visible = false;
                R.Retired.Add($"{was} → {nm}(«{who}»가 씀)");
            }
            catch (Exception ex) { R.Failed.Add($"{NameOf(tr, id)} 물러남 {ex.Message}"); }
        }
        return R;
    }

    static void WriteStamp(Transaction tr, DBObject o, params string[] vals)
    {
        if (o.ExtensionDictionary.IsNull) o.CreateExtensionDictionary();
        var d = (DBDictionary)tr.GetObject(o.ExtensionDictionary, OpenMode.ForWrite);
        var xr = new Xrecord { Data = new ResultBuffer(vals.Select(v => new TypedValue((int)DxfCode.Text, v ?? "")).ToArray()) };
        if (d.Contains(Key)) tr.GetObject(d.GetAt(Key), OpenMode.ForWrite).Erase();
        d.SetAt(Key, xr);
        tr.AddNewlyCreatedDBObject(xr, true);
    }

    /// <summary>같은 이름이 없게(지운 면은 건너뜀) — 겹치면 «_2» · «_3» ….</summary>
    internal static string UniqueName(Database db, Transaction tr, string baseName)
    {
        var have = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var cdoc = Autodesk.Civil.ApplicationServices.CivilDocument.GetCivilDocument(db);
        foreach (ObjectId id in cdoc.GetSurfaceIds())
        {
            if (id.IsNull || id.IsErased) continue;
            try { if (tr.GetObject(id, OpenMode.ForRead) is CivilDb.Surface s) have.Add(s.Name); } catch { }
        }
        if (!have.Contains(baseName)) return baseName;
        for (int i = 2; ; i++) { string c = $"{baseName}_{i}"; if (!have.Contains(c)) return c; }
    }

    /// <summary>도면의 물러난 면 전부(지운 것 빼고) — 초기화가 지울 목록에 넣어 아무도 안 쓰면 지움(쌓이지 않게).</summary>
    internal static List<ObjectId> All(Database db, Transaction tr)
    {
        var l = new List<ObjectId>();
        var cdoc = Autodesk.Civil.ApplicationServices.CivilDocument.GetCivilDocument(db);
        foreach (ObjectId id in cdoc.GetSurfaceIds())
        {
            if (id.IsNull || id.IsErased) continue;
            try { if (tr.GetObject(id, OpenMode.ForRead) is CivilDb.Surface s && IsRetired(s)) l.Add(id); } catch { }
        }
        return l;
    }
}
