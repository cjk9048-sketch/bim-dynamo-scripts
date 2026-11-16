using System.Text.RegularExpressions;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.Runtime;
using AcadApp = Autodesk.AutoCAD.ApplicationServices.Application;
using AcadEntity = Autodesk.AutoCAD.DatabaseServices.Entity;
using CivilDb = Autodesk.Civil.DatabaseServices;

namespace DH.Grading.Civil.Commands;

/// <summary>★[v117.18 · JACK 1116 «초기화가 없어»] <b>[터파기 초기화]</b> — 터파기 창이 만든 것만 지운다.
/// <para>지움: 터파기 기록(구조물 · 가시설 구간 · 날개) · 터파기 산출 지표면(이름 고정 — <see cref="ExcavCommand"/> 상수 · 조각 · 절토복원).
/// 남김: 터파기선 폴리선 · 원지반 · 계획지표면 · 기록이 가리키는 원지반 핸들의 면.</para>
/// <para>남이 쓰는 면(종단 등)은 <see cref="SurfaceRetire"/>가 지우지 않고 물러나게 한다 — 계획지표면 초기화와 같은 길(정지면은 안 건드림).</para></summary>
public sealed class ExcavResetCommand
{
    private static readonly Regex Virt = new("^" + Regex.Escape(ExcavCommand.VirtName) + @"\d+(_\d+)?$");
    private static readonly Regex CutBack = new(@"^터파기_절토복원\d+_\d+_DH(_\d+)?$");

    internal static bool Ours(string nm) =>
        SectionCommand.IsBase(nm, ExcavCommand.SurfName) || SectionCommand.IsBase(nm, ExcavCommand.BaseName)
        || SectionCommand.IsBase(nm, ViewSurfaceCommand.AllName) || SectionCommand.IsBase(nm, ViewSurfaceCommand.ExcavAllName)
        || Virt.IsMatch(nm) || CutBack.IsMatch(nm);

    [CommandMethod("DHEXCAVRESET", CommandFlags.Modal)]
    public void Reset()
    {
        var doc = AcadApp.DocumentManager.MdiActiveDocument;
        if (doc == null) return;
        var ed = doc.Editor;
        const string label = "터파기 초기화";
        GradingSettings.SyncToDocument(doc);
        if (!PickSession.Begin(ed, label)) return;
        try
        {
            int recN; var victims = new System.Collections.Generic.List<ObjectId>();
            var keepIds = new System.Collections.Generic.HashSet<ObjectId>();
            using (var tr = doc.Database.TransactionManager.StartTransaction())
            {
                var recs = ExcavBundleStore.TryLoadAll(doc.Database, tr, out _, out bool tooNew);
                if (tooNew) { ed.WriteMessage($"\n[{label}] 터파기 기록 못 읽음 — 더 새 애드인이 만든 기록 → 애드인 업데이트 뒤 다시"); return; }
                recN = recs?.Count ?? 0;
                // 지우면 안 되는 면 — 기록이 가리키는 원지반 · 지금 고른 원지반
                if (recs != null) foreach (var r in recs) { var g = NoriCommand.FindByHandle(doc.Database, r.GroundHandle); if (!g.IsNull) keepIds.Add(g); }
                var lg = NoriCommand.FindByHandle(doc.Database, GradingSettings.LastGroundHandle); if (!lg.IsNull) keepIds.Add(lg);
                var civ = Autodesk.Civil.ApplicationServices.CivilDocument.GetCivilDocument(doc.Database);
                foreach (ObjectId sid in civ.GetSurfaceIds())
                {
                    if (sid.IsNull || sid.IsErased || keepIds.Contains(sid)) continue;
                    if (tr.GetObject(sid, OpenMode.ForRead) is CivilDb.Surface s && Ours(s.Name ?? "")) victims.Add(sid);
                }
                tr.Commit();
            }
            if (recN == 0 && victims.Count == 0) { ed.WriteMessage($"\n[{label}] 지울 터파기 없음"); return; }
            if (!ResetCommand.Ask("터파기 결과 전부 지움 — 터파기선은 남김 → 계속?\n\n"
                                + $"· 터파기 지표면 {victims.Count}개 · 기록 {recN}개(가시설 구간 포함)\n"
                                + "· 터파기선 · 원지반 · 계획지표면은 그대로\n\n예: 지움     아니오: 그대로", "DH 터파기 — 초기화"))
            { ed.WriteMessage($"\n[{label}] 취소"); return; }

            using var job = new SelfCheckLog.Job(label, doc.Name);
            SurfaceRetire.Result ret; bool cleared; string dropped;
            using (var tr = doc.Database.TransactionManager.StartTransaction())
            {
                var P = SurfaceRetire.MustStay(doc.Database, tr, victims);
                dropped = DropRefs(doc.Database, tr, victims, P);   // ★[1117] 우리 종단 · 횡단 묶음이 지울 면을 쓰고 있으면 먼저 뗀다(HumanKeep은 우리 것을 안 봄)
                ret = SurfaceRetire.Apply(doc.Database, tr, victims, P);
                cleared = ExcavBundleStore.Clear(doc.Database, tr);
                tr.Commit();
            }
            try { ViewSurfaceCommand.ShowAll(); } catch { }
            try { ed.Regen(); } catch { }
            if (!SelfCheckLog.Headless) try { ExcavPalette.Refresh(); } catch { }
            string msg = $"터파기 지표면 {ret.Erased}개 · 기록 {(cleared ? recN : 0)}개 지움 → 터파기선 · 원지반은 남음"
                       + (dropped.Length > 0 ? " · " + dropped : "")
                       + (ret.Retired.Count + ret.Failed.Count > 0 ? " · ⚠남이 쓰는 면은 이름만 바꿔 숨김 → 로그 확인" : "");
            ed.WriteMessage($"\n[{label}] {msg}");
            try { DiagLog.Append($"\n■ DHEXCAVRESET — {msg}\n  {ret.Text}\n  {string.Join(" · ", ret.ErasedNames.Take(40))}\n"); } catch { }
            job.Done(ret.Failed.Count == 0, msg);
        }
        catch (System.Exception ex)
        {
            ed.WriteMessage($"\n[{label}] 계산 실패(프로그램 문제) → 다시 · 계속되면 로그 전달");
            try { DiagLog.Append($"\n■ 터파기 초기화 예외 — {ex.GetType().Name}: {ex.Message}\n{ex.StackTrace}\n"); } catch { }
        }
        finally { PickSession.End(); }
    }

    /// <summary>★[1117 · 검토 MED «초기화 뒤 종단 · 횡단이 지운 터파기면을 가리킴»] 지울 면(<paramref name="victims"/>)을 원천으로 쓰는 <b>우리 것</b>을 면을 지우기 앞에 뗀다 — 우리 표본선 묶음(+딸린 횡단도) · 우리 표면 종단.
    /// <para>원인: <see cref="SurfaceRetire.HumanKeep"/>은 우리 노선(«DH선형» · 진입도로) · 우리 묶음을 «우리 것 = 따로 치움»으로 건너뛰는데, 터파기 초기화는 그 «따로 치움»(전체 초기화의 EraseSectionObjects)을 안 불렀다 →
    /// 터파기면이 지워진 채 종단(DH_터파기) · 횡단 묶음의 원천이 남았다(Civil이 나중에 읽다 죽음).
    /// 묶음은 원지반 · 정지면 횡단까지 함께 지워진다(원천 하나만 빼는 길은 Civil이 지운 원천을 물던 1013 꼴이라 안 씀 — GroundDeps의 «횡단 묶음 지움»과 같은 길) → [종단도] · [횡단도] 다시.
    /// 사람 노선의 종단이 이 면을 원천으로 쓰면 <paramref name="stay"/>에 넣어 지우지 않고 물러나게 한다(묶음은 HumanKeep이 이미 봄) · 못 읽으면 지울 면 모두 물러남.</para>
    /// 돌려줌 = 명령창 한 줄(없으면 빈 글).</summary>
    internal static string DropRefs(Database db, Transaction tr, ICollection<ObjectId> victims, Dictionary<ObjectId, string> stay)
    {
        var V = new HashSet<ObjectId>(victims.Where(v => !v.IsNull && !v.IsErased));
        if (V.Count == 0) return "";
        void KeepAll(string who) { foreach (var v in V) if (!stay.ContainsKey(v)) stay[v] = who; }
        // ★[1117 검토 LOW] 두 판 — ①다 읽어 모음(지우지 않음) ②하나도 못 읽은 게 없을 때만 지움 · 못 읽으면 지울 면 모두 물러남 + 지우기 0
        var erasePf = new List<ObjectId>();
        var eraseGrp = new List<(ObjectId Id, List<ObjectId> Lines)>();
        var roadXsec = new List<ObjectId>();
        var seeds = new Dictionary<ObjectId, string>();
        try
        {
            var civ = Autodesk.Civil.ApplicationServices.CivilDocument.GetCivilDocument(db);
            foreach (ObjectId aid in civ.GetAlignmentIds())
            {
                if (aid.IsNull || aid.IsErased) continue;
                try
                {
                    if (tr.GetObject(aid, OpenMode.ForRead) is not CivilDb.Alignment al) continue;
                    bool own = RoadBuilder.IsOurs(al) || (al.Name ?? "").StartsWith(SectionCommand.AlignBase, System.StringComparison.OrdinalIgnoreCase);
                    var mine = new HashSet<string>(System.StringComparer.OrdinalIgnoreCase);
                    var d = RoadDesign.Read(tr, aid);
                    if (d != null) { mine.Add(d.SheetGroup); mine.Add(d.XsecGroup); mine.Add(d.PlanProfile); mine.Add(d.BaseProfile); mine.Add(d.SheetGround); }
                    mine.Remove("");
                    bool Ours(string handle, string? layer) => own || mine.Contains(handle) || (layer ?? "").StartsWith(RoadBuilder.AlignBase + "-", System.StringComparison.Ordinal);
                    // ★[1117 검토 MED] 도로 기록이 가리키는 «-바탕» · 지면 종단 · 시트 묶음은 지우면 기록 · 종단뷰 띠가 지운 것을 가리킴 → 원천 면을 물러나게(GroundDeps와 같은 길)
                    bool RoadKeep(string handle) => d != null && handle.Length > 0 &&
                        (string.Equals(handle, d.BaseProfile, System.StringComparison.OrdinalIgnoreCase) || string.Equals(handle, d.SheetGround, System.StringComparison.OrdinalIgnoreCase)
                         || string.Equals(handle, d.PlanProfile, System.StringComparison.OrdinalIgnoreCase) || string.Equals(handle, d.SheetGroup, System.StringComparison.OrdinalIgnoreCase));
                    // 종단 — 표면 종단(EG)의 원천 면은 DataSourceId(GroundDeps와 같은 읽기)
                    foreach (ObjectId pid in al.GetProfileIds())
                    {
                        if (pid.IsNull || pid.IsErased || tr.GetObject(pid, OpenMode.ForRead) is not CivilDb.Profile pf || pf.ProfileType != CivilDb.ProfileType.EG) continue;
                        if (pf.GetType().GetProperty("DataSourceId")?.GetValue(pf) is not ObjectId src) throw new System.Exception("표면 종단의 원천을 못 읽음");
                        if (!V.Contains(src)) continue;
                        string ph = pid.Handle.ToString();
                        if (Ours(ph, pf.Layer) && !RoadKeep(ph)) erasePf.Add(pid);
                        else if (!seeds.ContainsKey(src)) seeds[src] = $"종단 «{pf.Name}»";
                    }
                    // 표본선 묶음 — 원천(켠 것)이 지울 면이면 묶음째(딸린 횡단도 · 측점선 포함) · 사람 묶음은 HumanKeep이 이미 물러남으로
                    foreach (ObjectId gid in al.GetSampleLineGroupIds())
                    {
                        if (gid.IsNull || gid.IsErased || tr.GetObject(gid, OpenMode.ForRead) is not CivilDb.SampleLineGroup g || !Ours(gid.Handle.ToString(), g.Layer)) continue;
                        // ★단면에서 원천을 읽음 — GetSectionSources()는 화면 없는 Civil(1103)에서 탐침이 만든 묶음을 읽다 죽었다(SurfaceRetire.HumanKeep과 같은 읽기)
                        var used = new HashSet<ObjectId>(); var lines = new List<ObjectId>();
                        foreach (ObjectId slid in g.GetSampleLineIds())
                        {
                            if (slid.IsNull || slid.IsErased || tr.GetObject(slid, OpenMode.ForRead) is not CivilDb.SampleLine sl) continue;
                            lines.Add(slid);
                            foreach (ObjectId secId in sl.GetSectionIds())
                                if (!secId.IsNull && !secId.IsErased && tr.GetObject(secId, OpenMode.ForRead) is CivilDb.Section sec && V.Contains(sec.SourceId)) used.Add(sec.SourceId);
                        }
                        if (used.Count == 0) continue;
                        string gh = gid.Handle.ToString();
                        if (d != null && string.Equals(gh, d.XsecGroup, System.StringComparison.OrdinalIgnoreCase)) { if (!roadXsec.Contains(aid)) roadXsec.Add(aid); }   // 횡단뷰 · 기록 칸까지 함께(EraseRoadXsec)
                        else if (RoadKeep(gh)) { foreach (var u in used) if (!seeds.ContainsKey(u)) seeds[u] = $"시트 묶음 «{g.Name}»"; }
                        else eraseGrp.Add((gid, lines));
                    }
                }
                catch (System.Exception ex) { throw new System.Exception($"노선 {aid.Handle}(못 읽음): {ex.Message}", ex); }
            }
        }
        catch (System.Exception ex)
        {
            erasePf.Clear(); eraseGrp.Clear(); roadXsec.Clear(); seeds.Clear();
            KeepAll("종단 · 횡단(못 읽음)");
            try { DiagLog.Append($"\n■ 터파기 초기화 — 종단 · 횡단 원천 못 읽음 → 면은 물러남(지운 것 0): {ex.Message}\n"); } catch { }
            return "";
        }
        // ② 지움 — 예외는 위로(부르는 쪽 트랜잭션이 커밋 못 하고 버려짐)
        foreach (var kv in seeds) if (!stay.ContainsKey(kv.Key)) stay[kv.Key] = kv.Value;
        foreach (var pid in erasePf) tr.GetObject(pid, OpenMode.ForWrite, false, true).Erase();
        int nXs = ResetCommand.EraseRoadXsec(db, tr, roadXsec);
        if (eraseGrp.Count > 0)
        {
            // 묶음에 딸린 횡단뷰는 묶음과 함께 지워지는지 안 재서(1117) 직접 먼저 지움 — 측점선 → 횡단뷰(SampleLineId)
            var lineSet = new HashSet<ObjectId>(eraseGrp.SelectMany(x => x.Lines));
            var ms = (BlockTableRecord)tr.GetObject(SymbolUtilityServices.GetBlockModelSpaceId(db), OpenMode.ForRead);
            var views = new List<ObjectId>();
            foreach (ObjectId id in ms)
                if (!id.IsErased && tr.GetObject(id, OpenMode.ForRead) is CivilDb.SectionView sv && lineSet.Contains(sv.SampleLineId)) views.Add(id);
            foreach (var id in views) tr.GetObject(id, OpenMode.ForWrite, false, true).Erase();
            foreach (var g in eraseGrp) tr.GetObject(g.Id, OpenMode.ForWrite, false, true).Erase();
        }
        // ★[1117 검토 HIGH] 사람 종단 · 시트 묶음이 더한 씨앗의 소스도 따라가 물러남(MustStay는 이미 닫혔음)
        SurfaceRetire.Close(tr, V, stay);
        int nPf = erasePf.Count, nGr = eraseGrp.Count + nXs;
        return nPf + nGr == 0 ? "" : $"종단 {nPf}개 · 횡단 묶음 {eraseGrp.Count}개{(nXs > 0 ? $" · 도로 횡단 {nXs}개" : "")} 지움 → [종단도] · [횡단도] 다시";
    }

    /// <summary>★[v117.18 · JACK 1116 «지표면 생성 후 구배 변경시 업데이트할 버튼»] <b>[업데이트]</b> — 만든 터파기를 창의 지금 값(구배 · 기준면 · 원지반)으로 다시 만든다.
    /// 만들기와 같은 길(<see cref="ExcavCommand.DoExcav"/> · keepSpec 없이 = 지금 값) · 가시설 구간 · 날개는 기록에서 물려받음(ZonesStillFit).
    /// 터파기선을 다시 안 골라도 된다 — 구조물이 하나면 그것 · 여럿이면 고른 것.</summary>
    [CommandMethod("DHEXCAVUPDATE", CommandFlags.Modal)]
    public void Update()
    {
        var doc = AcadApp.DocumentManager.MdiActiveDocument;
        if (doc == null) return;
        var ed = doc.Editor; var db = doc.Database;
        const string label = "터파기 업데이트";
        GradingSettings.SyncToDocument(doc);
        PickSession.Hook();
        if (!PickSession.Begin(ed, label)) return;
        try
        {
            System.Collections.Generic.List<ExcavBundle>? recs;
            using (var tr = db.TransactionManager.StartTransaction())
            {
                recs = ExcavBundleStore.TryLoadAll(db, tr, out _, out bool tooNew);
                tr.Commit();
                if (tooNew) recs = null;
            }
            if (recs == null || recs.Count == 0) { ed.WriteMessage($"\n[{label}] 만든 터파기 없음 → [터파기 지표면 생성] 먼저"); return; }
            var poly = PickSession.ResolveForUse(doc, PickSession.KeyExcav);
            ExcavBundle? rec = null;
            if (!poly.IsNull) rec = recs.Find(r => r.PolyHandle == poly.Handle.ToString());
            else if (recs.Count == 1) rec = recs[0];
            if (rec == null)
            {
                ed.WriteMessage(poly.IsNull ? $"\n[{label}] 구조물 {recs.Count}개 — 어느 것인지 모름 → 터파기선 선택 뒤 다시"
                                            : $"\n[{label}] 이 터파기선은 아직 안 만듦 → [터파기 지표면 생성]");
                return;
            }
            poly = NoriCommand.FindByHandle(db, rec.PolyHandle);
            if (poly.IsNull) { ed.WriteMessage($"\n[{label}] 터파기선 지워짐 → 터파기선 다시 선택 뒤 [생성]"); return; }
            // ★[v117.19 · JACK 1116] 이 창에서 고른 것 → 이 터파기 기록의 원지반 → 하나면 자동 · 둘 이상이면 막음
            var ground = GroundSet.ForExcav(doc, rec.PolyHandle, out bool needGround, out _);
            if (needGround) { ed.WriteMessage($"\n[{label}] 원지반 2개 이상 → [원지반 선택]"); try { ExcavPalette.Say("원지반 2개 이상 → [원지반 선택]"); } catch { } return; }
            if (ground.IsNull) { ed.WriteMessage($"\n[{label}] 원지반 못 찾음 → [원지반 선택]"); return; }
            bool made;
            try
            {
                using (ViewSurfaceCommand.Focus(db, null)) made = ExcavCommand.DoExcav(doc, poly, ground);
                ViewSurfaceCommand.ShowAll();
            }
            catch (System.Exception ex)
            {
                string msg = ex.GetType() == typeof(System.Exception) ? ex.Message.Split('\n')[0].Trim() : "계산 실패(프로그램 문제) → 다시 · 계속되면 로그 전달";
                ed.WriteMessage($"\n[{label}] 못 만듦 — " + msg);
                try { DiagLog.Append($"\n■ 터파기 업데이트 예외 — {ex.GetType().Name}: {ex.Message}\n{ex.StackTrace}\n"); } catch { }
                try { ViewSurfaceCommand.ShowAll(); } catch { }
                made = false;
            }
            if (made) { PickSession.ClearAll(doc); ed.WriteMessage($"\n[{label}] 구배 1:{ExcavCommand.Slope:0.##} · 기준면 {(ExcavCommand.Basis == DH.Grading.Core.CrossSectionArea.ExcavBase.Plan ? "계획지표면" : "원지반")} → 다시 만듦" + (ExcavCommand.ZonesDroppedFor(rec.PolyHandle) ? "" : " · 가시설 구간 유지")); }   // ★[v117.18 설치 전 검토 LOW] 버렸으면(위 ⚠) «유지»라 하지 않음
            if (!SelfCheckLog.Headless) try { ExcavPalette.Refresh(); } catch { }
        }
        finally { PickSession.End(); }
    }
}
