using Autodesk.AutoCAD.ApplicationServices;
using Autodesk.AutoCAD.DatabaseServices;
using CivilDb = Autodesk.Civil.DatabaseServices;
using DH.Grading.Civil.Commands;

namespace DH.Grading.Civil;

/// <summary>★★[JACK 1116 «원지반을 지울 때 그 원지반을 쓰는 결과만»] 지울 원지반(G)에 <b>기대는 결과</b>만 가려 지운다 — 나머지는 그대로.
/// <para><b>의존 규칙.</b> ① 정지면(+ 부지 결과 · 종단 · 횡단 · 옹벽 · 터파기 기준면 합성) = 붙여넣기 소스 또는 정지 기록의 원지반이 G ② 진입도로마다 = 기록의 원지반 핸들 · 바탕면 이름이 G
/// (정지면 위 도로는 정지면이 가면 같이 · 핸들은 안 봄 — «정지면이 바탕이면 안 씀») ③ 터파기마다 = 기록의 원지반이 G · 또는 계획면 기준(Base=1)인데 정지면이 감 ④ 최종지표면 = 정지면 · 도로가 바뀌면 다시 짓거나(남는 부분이 있으면) 지움.
/// ⑤ <b>남는 도로의 횡단 묶음 · 종단이 지워질 면을 원천으로 물면</b>(정지면 가족 · 고른 원지반 · 가는 도로의 면 — 본 도로 면을 가지 도로가 물음): 종단이 물면 그 도로도 같이 · 횡단만 물면 횡단(묶음 + 뷰)만 지움.</para>
/// <para><b>차례.</b> 남는 도로의 횡단 → 도로 → 터파기(면 · 기록 걷고 남는 것은 나중에 다시) → 정지면 + 원지반 한 번에(<see cref="ResetCommand.Wipe"/>) 또는 원지반만 → 재료 → 터파기 다시 → 최종지표면 · 노리선.
/// 의존을 못 가리면(면 정의 못 읽음 · 터파기 기록 못 읽음 · 도로의 바탕을 모름 · 도로 기록 못 읽음) <see cref="Plan.Fallback"/> — 부르는 쪽이 종전의 전체 지움으로.
/// 단계마다 따로 커밋되므로 도중에 실패하면 거기서 멈추고(원지반은 지우지 않음) 알린다.</para></summary>
internal static class GroundDeps
{
    internal sealed class Plan
    {
        /// <summary>비면 일부만 · 있으면 이유(= 전체 지움으로 돌아감).</summary>
        public string Fallback = "";
        public bool Pad, PadPresent, FinalExisted;
        public readonly List<(ObjectId Id, string Name, string Why)> RoadsGo = new(), RoadsStay = new();
        public readonly List<(ExcavBundle Rec, string Why)> ExcavGo = new(), ExcavStay = new();
        /// <summary>남는 도로 가운데 횡단(묶음 + 뷰)만 지울 것 — 그 묶음이 지워질 면을 원천으로 물고 있음.</summary>
        public readonly List<(ObjectId Id, string Name)> XsecDrop = new();
        /// <summary>남이 쓰는(사람 표본선 · 남의 면 · 안 지워지는 우리 파생 면) 이름 — 원지반은 지워지지 않고 이름만 바꿔 숨음.</summary>
        public string Human = "";
        public bool Final => Pad || RoadsGo.Count > 0;
        public bool Any => Pad || RoadsGo.Count > 0 || ExcavGo.Count > 0 || XsecDrop.Count > 0;

        public string Gone()
        {
            var l = new List<string>();
            if (Pad) l.Add("정지면");
            if (RoadsGo.Count > 0) l.Add("진입도로 " + RoadsGo.Count);
            if (ExcavGo.Count > 0) l.Add("터파기 " + ExcavGo.Count);
            if (XsecDrop.Count > 0) l.Add("진입도로 " + XsecDrop.Count + " 횡단만");
            return string.Join(" · ", l);
        }

        public string Stay()
        {
            var l = new List<string>();
            foreach (var g in RoadsStay.GroupBy(r => r.Why)) l.Add($"진입도로 {g.Count()}" + (g.Key.Length > 0 ? $" ({g.Key})" : ""));
            foreach (var g in ExcavStay.GroupBy(r => r.Why)) l.Add($"터파기 {g.Count()}" + (g.Key.Length > 0 ? $" ({g.Key})" : ""));
            return string.Join(" · ", l);
        }
    }

    internal static bool IsSitePad(string nm)
        => !ExcavResetCommand.Ours(nm) && ResetCommand.SiteBaseNames.Any(b => SectionCommand.IsBase(nm, b));

    /// <summary>표본선 묶음(핸들)의 단면 원천들 — 못 읽으면 거짓(부르는 쪽이 전체 지움으로). 묶음이 없으면(지워짐 · 빈 핸들) 참 · 원천 없음.</summary>
    static bool GroupSources(Transaction tr, Database db, string handle, List<ObjectId> into)
    {
        var gid = NoriCommand.FindByHandle(db, handle);
        if (gid.IsNull) return true;
        try
        {
            if (tr.GetObject(gid, OpenMode.ForRead) is not CivilDb.SampleLineGroup g) return true;
            foreach (ObjectId slid in g.GetSampleLineIds())
            {
                if (slid.IsNull || slid.IsErased || tr.GetObject(slid, OpenMode.ForRead) is not CivilDb.SampleLine sl) continue;
                foreach (ObjectId secId in sl.GetSectionIds())
                    if (!secId.IsNull && !secId.IsErased && tr.GetObject(secId, OpenMode.ForRead) is CivilDb.Section sec && !sec.SourceId.IsNull) into.Add(sec.SourceId);
            }
            return true;
        }
        catch { return false; }
    }

    /// <summary>읽기만 — G(지울 원지반들)에 기대는 것을 가린다.</summary>
    internal static Plan Resolve(Document doc, ICollection<ObjectId> G)
    {
        var p = new Plan();
        var db = doc.Database;
        try
        {
            using var tr = db.TransactionManager.StartTransaction();
            var gh = new HashSet<string>(G.Select(g => g.Handle.ToString()), StringComparer.OrdinalIgnoreCase);
            var gSet = new HashSet<ObjectId>(G);
            var gName = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);   // 핸들 → 이름
            foreach (var g in G) try { gName[g.Handle.ToString()] = (tr.GetObject(g, OpenMode.ForRead) as CivilDb.Surface)?.Name ?? ""; } catch { }

            // ① 정지면 — 붙여넣기 소스(면 정의) · 정지 기록의 원지반
            var users = SurfaceRetire.MustStay(db, tr, G);
            var human = new List<string>(); var userNames = new HashSet<string>(); var derived = new List<string>();
            foreach (var kv in users)
                foreach (var who in kv.Value.Split(" · "))
                {
                    string w = who.Trim();
                    if (w.Length == 0) continue;
                    if (w.Contains("(못 읽음)")) { p.Fallback = "면 정의를 못 읽음"; continue; }
                    userNames.Add(w);
                    if (IsSitePad(w)) p.Pad = true;
                    else if (ExcavResetCommand.Ours(w) || FinalSurfaceBuilder.IsOldName(w)
                             || SectionCommand.IsBase(w, FinalSurfaceBuilder.Name) || SectionCommand.IsBase(w, FinalSurfaceBuilder.TempName)
                             || RoadSession.SurfBase.Length > 0 && SectionCommand.IsBase(w, RoadSession.SurfBase)) derived.Add(w);   // 터파기 · 최종면 · 도로 면 — 아래 규칙으로 따로 가림 · 안 지워지면 «남이 씀»
                    else human.Add(w);
                }
            var regs = GradingBundleStore.TryLoadAll(db, tr, out _);
            if (regs != null) foreach (var r in regs) if (!string.IsNullOrEmpty(r.GroundHandle) && gh.Contains(r.GroundHandle)) p.Pad = true;
            var civ = Autodesk.Civil.ApplicationServices.CivilDocument.GetCivilDocument(db);
            foreach (ObjectId sid in civ.GetSurfaceIds())
                try { if (!sid.IsErased && tr.GetObject(sid, OpenMode.ForRead) is CivilDb.Surface s && SectionCommand.IsBase(s.Name ?? "", SectionCommand.PadSurfaceBase)) { p.PadPresent = true; break; } } catch { }
            if (regs is { Count: > 0 }) p.PadPresent = true;
            foreach (ObjectId sid in civ.GetSurfaceIds())
                try { if (!sid.IsErased && tr.GetObject(sid, OpenMode.ForRead) is CivilDb.Surface s && SectionCommand.IsBase(s.Name ?? "", FinalSurfaceBuilder.Name)) { p.FinalExisted = true; break; } } catch { }

            // ② 진입도로
            var rd = new Dictionary<ObjectId, RoadDesign>();
            var goSurf = new HashSet<ObjectId>();   // 가는 도로의 면 — 지워짐
            void AddGoSurf(RoadDesign d) { var sf = string.IsNullOrEmpty(d.SurfaceHandle) ? ObjectId.Null : NoriCommand.FindByHandle(db, d.SurfaceHandle); if (!sf.IsNull) goSurf.Add(sf); }
            foreach (var aid in RoadRanges.Roads(tr, db))
                try
                {
                    string nm = (tr.GetObject(aid, OpenMode.ForRead) as CivilDb.Alignment)?.Name ?? "";
                    var d = RoadDesign.Read(tr, aid);
                    if (d == null) { p.Fallback = p.Fallback.Length > 0 ? p.Fallback : $"도로 «{nm}»의 기록을 못 읽음"; continue; }
                    rd[aid] = d;
                    string bs = d.BaseSurface ?? "";
                    bool onPad = IsSitePad(bs) && bs.Length > 0;
                    string why;
                    bool go;
                    if (onPad) { go = p.Pad; why = "정지면 위"; }
                    else
                    {
                        bool byHandle = !string.IsNullOrEmpty(d.GroundHandle) && gh.Contains(d.GroundHandle);
                        bool byName = bs.Length > 0 && gName.Values.Contains(bs);
                        var sid = string.IsNullOrEmpty(d.SurfaceHandle) ? ObjectId.Null : NoriCommand.FindByHandle(db, d.SurfaceHandle);
                        bool bySrc = !sid.IsNull && userNames.Contains((tr.GetObject(sid, OpenMode.ForRead) as CivilDb.Surface)?.Name ?? "\u0001");   // 도로 면이 원지반을 붙여넣은 경우
                        go = byHandle || byName || bySrc;
                        why = bs.Length > 0 ? bs + " 위" : "";
                        if (!go && bs.Length == 0 && string.IsNullOrEmpty(d.GroundHandle)) p.Fallback = p.Fallback.Length > 0 ? p.Fallback : $"도로 «{nm}»의 바탕을 모름";
                    }
                    (go ? p.RoadsGo : p.RoadsStay).Add((aid, nm, why));
                    if (go) AddGoSurf(d);
                }
                catch { p.Fallback = p.Fallback.Length > 0 ? p.Fallback : "도로 기록을 못 읽음"; }
            // 기록 없는 우리 노선 — 남는 도로 판정에서 빠지므로 일부만으로 못 가름(종전의 전체 지움이 걷음)
            foreach (ObjectId aid in Autodesk.Civil.ApplicationServices.CivilDocument.GetCivilDocument(db).GetAlignmentIds())
                try { if (!aid.IsErased && tr.GetObject(aid, OpenMode.ForRead) is CivilDb.Alignment al && RoadBuilder.IsOurs(al) && !RoadDesign.Has(tr, aid)) p.Fallback = p.Fallback.Length > 0 ? p.Fallback : $"기록 없는 도로 노선 «{al.Name}»"; } catch { }

            // ③ 터파기
            var recs = ExcavBundleStore.TryLoadAll(db, tr, out _, out bool tooNew);
            if (tooNew) p.Fallback = p.Fallback.Length > 0 ? p.Fallback : "터파기 기록을 못 읽음";
            else if (recs != null)
                foreach (var r in recs)
                {
                    bool byG = !string.IsNullOrEmpty(r.GroundHandle) && gh.Contains(r.GroundHandle);
                    bool byPad = r.Base == 1 && p.Pad;
                    string gn = gName.TryGetValue(r.GroundHandle ?? "", out var n0) ? n0 : GroundSet.NameOf(db, NoriCommand.FindByHandle(db, r.GroundHandle ?? ""));
                    if (byG || byPad) p.ExcavGo.Add((r, byG ? gn : "정지면 기준"));
                    else
                    {
                        if (string.IsNullOrEmpty(r.GroundHandle) && r.Base == 0) p.Fallback = p.Fallback.Length > 0 ? p.Fallback : "터파기의 원지반을 모름";
                        p.ExcavStay.Add((r, gn));
                    }
                }

            // ④ 남는 도로의 횡단 묶음 · 종단이 «지워질 면»을 원천으로 무나 — 지워진 원천을 문 채 남으면 Civil이 나중에 읽다 죽는다
            //   지워질 면 = 고른 원지반 · 가는 도로의 면 · (정지면이 가면) 정지면 가족 · 최종면 · 우리 면 이름 · (터파기가 가면) 터파기 면. 안전하게 넓게 잡는다(물러남이어도 «지워짐»으로 봄).
            if (p.Fallback.Length == 0)
            {
                bool WillErase(ObjectId sid)
                {
                    if (sid.IsNull || sid.IsErased) return false;
                    if (gSet.Contains(sid) || goSurf.Contains(sid)) return true;
                    string nm = (tr.GetObject(sid, OpenMode.ForRead) as CivilDb.Surface)?.Name ?? "";
                    if (p.Pad && (ResetCommand.WipesSiteName(nm) || ExcavResetCommand.Ours(nm))) return true;
                    return p.ExcavGo.Count > 0 && ExcavResetCommand.Ours(nm);
                }
                bool changed;
                do
                {
                    changed = false;
                    foreach (var r in p.RoadsStay.ToList())
                    {
                        bool toGo = false, xs = false;
                        try
                        {
                            var d = rd[r.Id];
                            var al = (CivilDb.Alignment)tr.GetObject(r.Id, OpenMode.ForRead);
                            foreach (ObjectId pid in al.GetProfileIds())
                                if (tr.GetObject(pid, OpenMode.ForRead) is CivilDb.Profile pf && pf.ProfileType == CivilDb.ProfileType.EG)   // «-바탕» 표면 종단 — 원천 면은 DataSourceId(UndoGuard와 같은 읽기)
                                {
                                    var ds = pf.GetType().GetProperty("DataSourceId")?.GetValue(pf);
                                    if (ds is ObjectId dsid) { if (WillErase(dsid)) toGo = true; }
                                    else throw new System.Exception("표면 종단의 원천을 못 읽음");
                                }
                            var src = new List<ObjectId>();
                            if (!GroupSources(tr, db, d.SheetGroup, src)) throw new System.Exception("종단도 묶음을 못 읽음");
                            if (src.Any(WillErase)) toGo = true;
                            src.Clear();
                            if (!GroupSources(tr, db, d.XsecGroup, src)) throw new System.Exception("횡단 묶음을 못 읽음");
                            xs = src.Any(WillErase);
                        }
                        catch (System.Exception ex) { p.Fallback = p.Fallback.Length > 0 ? p.Fallback : $"도로 «{r.Name}»의 종단 · 횡단 원천을 못 읽음 — {ex.Message}"; }
                        if (toGo)
                        {
                            p.RoadsStay.Remove(r); p.RoadsGo.Add((r.Id, r.Name, "종단이 지워질 면을 따름")); AddGoSurf(rd[r.Id]);
                            p.XsecDrop.RemoveAll(x => x.Id == r.Id);
                            changed = true;
                        }
                        else if (xs && !p.XsecDrop.Any(x => x.Id == r.Id)) p.XsecDrop.Add((r.Id, r.Name));
                    }
                } while (changed);

                // 안 지워지는 우리 파생 면(전체면 · 최종면 · 남는 도로 면)이 원지반을 붙여 쓰면 «남이 씀» — 원지반은 이름만 바꿔 숨음
                var goRoadSurfNames = new HashSet<string>(goSurf.Select(s => (tr.GetObject(s, OpenMode.ForRead) as CivilDb.Surface)?.Name ?? "\u0001"));
                foreach (var w in derived.Distinct())
                {
                    bool isFinal = FinalSurfaceBuilder.IsOldName(w) || SectionCommand.IsBase(w, FinalSurfaceBuilder.Name) || SectionCommand.IsBase(w, FinalSurfaceBuilder.TempName);
                    bool erased = p.Pad && (ResetCommand.WipesSiteName(w) || ExcavResetCommand.Ours(w))
                               || ExcavResetCommand.Ours(w) && p.ExcavGo.Count > 0
                               || isFinal && p.Final
                               || goRoadSurfNames.Contains(w);
                    if (!erased) human.Add(w);
                }
            }
            p.Human = string.Join(" · ", human.Distinct());
            tr.Commit();
        }
        catch (System.Exception ex) { p.Fallback = "의존을 못 가림 — " + ex.Message; }
        return p;
    }

    /// <summary>지운다 — <see cref="Resolve"/>가 일부만으로 가린 뒤. <paramref name="only"/> = 고른 원지반(null = 전부 · G가 전부). 반환 = (한 일, 알림 — 없으면 빈 글).
    /// 단계마다 따로 커밋 — 앞 단계가 실패하면 원지반은 지우지 않고 멈춘다(지워진 원천을 문 결과를 안 남김). 되읽기는 항상.</summary>
    internal static (string Done, string Left) Execute(Document doc, Plan p, ICollection<ObjectId> G, ICollection<ObjectId>? only)
    {
        var db = doc.Database;
        var notes = new List<string>();
        int fail = 0, surfs = 0, ents = 0, nE = 0;
        string retire = "", stopAt = "";
        bool anyRoadsLeft = p.RoadsStay.Count > 0;
        var survive = p.ExcavStay.Select(e => e.Rec).ToList();
        bool excTouched = p.ExcavGo.Count > 0 || p.Pad;

        // 단계 — critical = 실패하면 멈춤(뒤 단계 안 함)
        void Step(string name, bool critical, Action a)
        {
            if (stopAt.Length > 0) return;
            try { a(); }
            catch (System.Exception ex)
            {
                notes.Add($"⚠{name} 실패 — {ex.Message}");
                try { DiagLog.Append($"\n■ 원지반 삭제 — {name} 실패: {ex}\n"); } catch { }
                if (critical) stopAt = name;
            }
        }

        // 0) 남는 도로의 횡단 — 지워질 면을 문 묶음 · 뷰(면을 지우기 전에)
        Step("횡단 지움", true, () =>
        {
            if (p.XsecDrop.Count == 0) return;
            using var tr = db.TransactionManager.StartTransaction();
            ents += ResetCommand.EraseRoadXsec(db, tr, p.XsecDrop.Select(x => x.Id).ToList());
            tr.Commit();
            notes.Add($"진입도로 {p.XsecDrop.Count} 횡단 지움 → [횡단도 생성] 다시");
        });

        // 1) 도로 — 쓰는 것부터(종단 · 선 · 면). 남는 도로가 없으면 종전의 초기화 길(고아 도로 면 · 표식 없는 DH도로- 객체까지) · 정지면이 가면 아래 3)의 Wipe가 같이
        Step("도로", true, () =>
        {
            if (p.RoadsGo.Count == 0 || p.Pad && !anyRoadsLeft) return;
            var rep = anyRoadsLeft ? ResetCommand.WipeRoads(doc, p.RoadsGo.Select(r => r.Id).ToList())
                                   : ResetCommand.Wipe(doc, ResetCommand.Scope.Road, includeImported: false, keepGround: false);
            surfs += rep.Surfs; ents += rep.Ents;
            try { DiagLog.Append($"\n■ 원지반 삭제 — 도로 {p.RoadsGo.Count}개: 지표면 {rep.Surfs} · 객체 {rep.Ents}\n  {rep.Log.ToString().Replace("\n", "\n  ")}\n"); } catch { }
            if (rep.Ret.Retired.Count + rep.Ret.Failed.Count > 0) retire = rep.RetireLine;
            if (anyRoadsLeft && rep.Tabs > 0) notes.Add("도면생성 탭 지움 → [도면생성] 다시");
        });

        // 2) 터파기 — 합성면은 기록 전부를 한 장으로 굽는다 → 지울 기록이 있으면 면을 모두 걷고 남는 기록만 다시 짓는다
        Step("터파기 면", true, () =>
        {
            if (p.ExcavGo.Count == 0 || p.Pad) return;
            using var tr = db.TransactionManager.StartTransaction();
            var victims = new List<ObjectId>();
            var civ = Autodesk.Civil.ApplicationServices.CivilDocument.GetCivilDocument(db);
            foreach (ObjectId sid in civ.GetSurfaceIds())
                try { if (!sid.IsNull && !sid.IsErased && !G.Contains(sid) && tr.GetObject(sid, OpenMode.ForRead) is CivilDb.Surface s && ExcavResetCommand.Ours(s.Name ?? "")) victims.Add(sid); } catch { }
            var P = SurfaceRetire.MustStay(db, tr, victims);
            var ret = SurfaceRetire.Apply(db, tr, victims, P);
            surfs += ret.Erased;
            if (ret.Retired.Count + ret.Failed.Count > 0) notes.Add("터파기 면 중 남이 쓰는 것은 이름만 바꿔 숨김");
            ExcavBundleStore.Clear(db, tr);
            if (survive.Count > 0) ExcavBundleStore.SaveAll(db, tr, survive);
            tr.Commit();
        });

        // 3) 정지면(+ 부지 결과) + 원지반 — 한 번에(물러남 규칙 · 기록 · 번들) / 정지면이 안 가면 원지반만
        int nGround = G.Count;
        Step("정지면 · 원지반", true, () =>
        {
            if (p.Pad)
            {
                var rep = ResetCommand.Wipe(doc, ResetCommand.Scope.All, includeImported: true, keepGround: false, onlyGround: only, keepGroundMaterial: true, keepRoads: anyRoadsLeft);
                surfs += rep.Surfs; ents += rep.Ents;
                try { DiagLog.Append($"\n■ 원지반 삭제 — 정지면 + 원지반: 지표면 {rep.Surfs} · 객체 {rep.Ents}\n  {rep.Log.ToString().Replace("\n", "\n  ")}\n"); } catch { }
                if (rep.Ret.Retired.Count + rep.Ret.Failed.Count > 0) retire = retire.Length > 0 ? retire + " · " + rep.RetireLine : rep.RetireLine;
            }
            else
            {
                using var tr = db.TransactionManager.StartTransaction();
                var V = G.Where(g => !g.IsErased).ToList();
                var P = SurfaceRetire.MustStay(db, tr, V);
                var ret = SurfaceRetire.Apply(db, tr, V, P);
                surfs += ret.Erased;
                if (ret.Retired.Count + ret.Failed.Count > 0) retire = retire.Length > 0 ? retire + " · " + ret.Text : ret.Text;
                // 원지반을 다 지우고 결과도 걷었으면 원지형 자르기 기록도(정지면이 가는 길의 Wipe와 같음)
                if (only == null && p.Any) GroundCropBuilder.ClearRecord(db, tr);
                tr.Commit();
            }
        });
        // 재료 — 정지면 · 도로를 걷은 뒤(물러난 원지반은 살아 있어 그 재료가 남음)
        Step("재료 지움", false, () => nE = GroundOffCommand.EraseMaterial(doc, ref fail));

        // 4) 남는 터파기 — 정지면이 갔으면 기록부터 되살리고(Wipe가 걷음) 합성면을 남은 기록으로 다시
        Step("터파기 다시", false, () =>
        {
            if (!excTouched || survive.Count == 0) return;
            if (p.Pad) { using var tr = db.TransactionManager.StartTransaction(); ExcavBundleStore.SaveAll(db, tr, survive); tr.Commit(); }
            if (survive.Select(r => r.Base).Distinct().Count() > 1) { notes.Add("남은 터파기의 기준면이 섞여 면을 다시 못 만듦 → [업데이트]"); return; }
            // 살아 있는 원지반의 기록을 고른다(다 같은 원지반으로 구움) — 계획면 기준 + 원지반 핸들 없음이면 남은 원지반이 하나일 때 그것
            var alive = new List<ObjectId>();
            using (var tr = db.TransactionManager.StartTransaction())
            {
                var civ = Autodesk.Civil.ApplicationServices.CivilDocument.GetCivilDocument(db);
                foreach (ObjectId sid in civ.GetSurfaceIds())
                    try { if (!sid.IsNull && !sid.IsErased && !G.Contains(sid) && tr.GetObject(sid, OpenMode.ForRead) is CivilDb.Surface s && SectionCommand.IsBase(s.Name ?? "", ImportGisCommand.GroundSurfaceName)) alive.Add(sid); } catch { }
                tr.Commit();
            }
            ExcavBundle? rec = null; ObjectId gr = ObjectId.Null;
            foreach (var r in survive)
            {
                var g = NoriCommand.FindByHandle(db, r.GroundHandle ?? "");
                if (!g.IsNull && !G.Contains(g) && !NoriCommand.FindByHandle(db, r.PolyHandle).IsNull) { rec = r; gr = g; break; }
            }
            if (rec == null && alive.Count == 1)
                foreach (var r in survive)
                    if (r.Base == 1 && string.IsNullOrEmpty(r.GroundHandle) && !NoriCommand.FindByHandle(db, r.PolyHandle).IsNull) { rec = r; gr = alive[0]; break; }
            if (rec == null) { notes.Add("남은 터파기는 터파기선 · 원지반을 못 찾아 면을 다시 못 만듦 → [업데이트]"); return; }
            if (survive.Select(r => r.GroundHandle ?? "").Where(h => h.Length > 0).Distinct().Count() > 1)
                notes.Add("남은 터파기의 원지반이 서로 달라 한 원지반으로 다시 지음 → [업데이트] 확인");
            var poly = NoriCommand.FindByHandle(db, rec.PolyHandle);
            bool made;
            // keepSpec: true — 살아남은 기록의 구배 · 하한 · 기준면을 덮어쓰지 않음(구간만 고치는 길과 같음) · 기준면 물음도 안 뜸
            using (ViewSurfaceCommand.Focus(db, null)) made = ExcavCommand.DoExcav(doc, poly, gr, keepSpec: true);
            try { ViewSurfaceCommand.ShowAll(); } catch { }
            if (!made) notes.Add("남은 터파기 면을 다시 못 만듦 → [업데이트]");
        });

        // 5) 최종지표면(정지면 + 도로 합성) · 부지 노리선 — 정지면이 남을 때만(갔으면 3)에서 이미 걷음) · 도로를 걷었으면 노리선은 최종면 유무와 무관
        Step("최종지표면", false, () =>
        {
            if (p.Pad || !p.Final) return;
            if (p.FinalExisted)
            {
                try { string fn = FinalSurfaceBuilder.Build(doc); if (fn.Length > 0) notes.Add("최종지표면: " + fn); } catch (System.Exception ex) { notes.Add("최종지표면을 다시 못 지음 — " + ex.Message); }
            }
            if (p.RoadsGo.Count > 0 && p.PadPresent) { try { NoriCommand.AutoDraw(doc); } catch { } }
        });

        // 되읽기 — 지운다고 한 것이 정말 없나(도중에 멈췄어도 한다)
        int leftG = 0;
        using (var tr2 = db.TransactionManager.StartTransaction())
        {
            foreach (var g in G) if (!g.IsErased) leftG++;
            foreach (var r in p.RoadsGo)
                try
                {
                    // 사람 노선은 노선을 남기고 기록만 지움 — «우리 노선이거나 기록이 남은 것»만 «남음»
                    if (r.Id.IsErased) continue;
                    if (tr2.GetObject(r.Id, OpenMode.ForRead) is CivilDb.Alignment al && (RoadBuilder.IsOurs(al) || RoadDesign.Has(tr2, r.Id))) notes.Add($"⚠도로 «{r.Name}» 남음");
                }
                catch { }
            tr2.Commit();
        }
        if (fail > 0) notes.Add($"⚠못 지움 {fail}개 (레이어 잠금 확인)");
        if (retire.Length > 0) notes.Add(retire);
        string done = $"원지반 {nGround - leftG}개 · 재료 {nE:N0}개" + (p.Any ? " · 함께 " + p.Gone() : "") + $" 지움 (지표면 {surfs} · 객체 {ents})";
        if (stopAt.Length > 0)
        {
            done = $"⚠{stopAt}에서 중간에 멈춤 — U로 되돌리기 (원지반 {nGround - leftG}개 · 지표면 {surfs} · 객체 {ents}까지 지움)";
            notes.Insert(0, "중간에 멈춤 — U로 되돌리기");
        }
        string st = p.Stay();
        if (st.Length > 0) notes.Insert(0, "남김: " + st);
        return (done, string.Join("\n  ", notes));
    }
}
