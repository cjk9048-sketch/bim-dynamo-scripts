using System.Linq;
using System.Text;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.Civil.DatabaseServices;
using DH.Grading.Core;
using Tri = DH.Grading.Core.WallDaylight.Tri;
using DBObject = Autodesk.AutoCAD.DatabaseServices.DBObject;
using Surface = Autodesk.Civil.DatabaseServices.Surface;

namespace DH.Grading.Civil;

/// <summary>★★★[v103.3 · JACK 1021 «경계를 기준으로 뒷채움거리와 사면경사로 터파기 지표면을(숨김상태로) 별도로 생성해서 종단과 횡단에서 보여주고 토공량에 반영»]
/// <b>옹벽 뒷채움 터파기면(<c>옹벽터파기_DH</c>)을 짓는다 — 종단을 그리는 명령이 부른다</b>(계획_v103_3.md 2판).
/// <para>①종단이 쓰는 정지면에 보이는 옹벽 앞면 아랫선을 모아(구간 옹벽 · 굳은 옹벽 · 번들 옹벽) ②그 면에서 벽 밑 높이 · 흙 쪽을 재고 ③바닥 띠(앞 · 뒤 · 기초 밑)마다
/// 경사 법면 조각을 Civil로 짓고 <b>삼각형만 읽어 곧바로 지운다</b> ④Core <see cref="WallDaylight.LowerEnvelope"/>가 조각들의 아래 껍질 중 기준면 Lower(정지면_DH, 원지반) 아래인 것만 남긴다
/// ⑤LandXML로 굳힌다(옹벽면과 같은 길 — 새 이름의 파일 · 되읽기 관문 · 굳힘). 붙여넣기 · 경계 · 스냅샷을 안 쓴다(메모리 1013의 죽음 길을 안 탄다).</para>
/// <para>면 도장(설정 · 정지면_DH 핸들 · 합성 번호 · 원지반 핸들 # 앞면 지문)이 같으면 되쓴다.</para></summary>
internal static class WallExcavBuilder
{
    internal const string Name = "옹벽터파기_DH";
    internal const string TempName = "옹벽터파기_짓는중_DH";
    internal const string PieceName = "옹벽터파기조각_DH";
    internal const string Label = "옹벽터파기";
    internal const string ProfName = "DH_옹벽터파기";
    const string StampKey = "DH_WALLEXCAV";
    /// <summary>선형 확장사전 — 종단이 쓴 터파기면 핸들 · 도장 · 상태 · 까닭(계획 2-4). <c>DH_PROFSURF</c>와 따로.</summary>
    internal const string AlignKey = "DH_PROFEXCAV";

    internal enum State { Off = 0, Built = 1, Failed = 2 }

    internal sealed class Result
    {
        public State State;
        public ObjectId SurfaceId = ObjectId.Null;
        public string SurfaceName = "";
        public string Stamp = "";
        public string Why = "";
        public bool Reused;
        public int Faces, Pieces;
        public WallExcav.Settings Settings = WallExcav.Settings.Default;
        /// <summary>★[v104 B] 배면사면 면(<see cref="WallBackBuilder"/>) — 상태 · 면 · 까닭 · 도장 · 지을 때 배면 설정 머리.</summary>
        public State BackState;
        public ObjectId BackId = ObjectId.Null;
        public string BackName = "", BackWhy = "", BackStamp = "", BackHead = "";
        public bool BackReused;
        /// <summary>터파기면에 배면 기록(수량용)을 적었나.</summary>
        public bool BackRec;
        public bool HasWarn => State == State.Failed || Why.Length > 0 || BackState == State.Failed;
        public string StatusText => (State switch
        {
            State.Off => "꺼짐",
            State.Built => $"지음({SurfaceName}{(Reused ? " · 되씀" : "")} · {Settings.Text})" + (Why.Length > 0 ? " · ⚠" + Why : ""),
            _ => "⚠못 지음 — " + Why,
        }) + BackText;
        public string BackText => BackState switch
        {
            State.Built => $" · 배면 {BackName}{(BackReused ? "(되씀)" : "")}" + (BackWhy.Length > 0 ? " ⚠" + BackWhy : ""),
            State.Failed => " · ⚠배면 못 지음 — " + BackWhy,
            _ => "",
        };
    }

    /// <summary>도장 머리 — 설정(켬/끔 · 여유 · 경사 · 소단 · 버림 · 형식 둘) · ★[v104 2판 · 검토 중간 3] 두 형식 표의 지문 · 정지면_DH 핸들 · 합성 번호 · 원지반 핸들(재검토 중간 5 · 6: InvariantCulture "R").</summary>
    static string HeadOf(WallExcav.Settings s, string tablesPrint, string planHandle, string nonce, string groundHandle) => $"{s.Head}|{tablesPrint}|{planHandle}|{nonce}|{groundHandle}";
    internal static string StampHead(string stamp) => string.IsNullOrEmpty(stamp) ? "" : stamp.Split('#')[0];

    /// <summary>도면의 절토 · 성토 형식 표(못 읽으면 첫 표 + 까닭을 <paramref name="notes"/>에).</summary>
    static (WallSection.Table Cut, WallSection.Table Fill) TablesOf(Database db, WallExcav.Settings s, System.Collections.Generic.List<string>? notes)
    {
        var c = WallListStore.SectionOf(db, s.CutKind, out string w1);
        var f = WallListStore.SectionOf(db, s.FillKind, out string w2);
        if (w1.Length > 0) notes?.Add($"절토 단면 표({s.CutKind}) 기록 — {w1} · 첫 표로 셈");
        if (w2.Length > 0 && s.FillKind != s.CutKind) notes?.Add($"성토 단면 표({s.FillKind}) 기록 — {w2} · 첫 표로 셈");
        return (c, f);
    }

    /// <summary>지금 도장 머리(종단을 다시 그리지 않고 견줄 때 — 횡단 · 검토선). 정지면_DH가 하나가 아니면 그 칸은 빈 값.</summary>
    internal static string CurrentHead(Database db, ObjectId groundId, out WallExcav.Settings s)
    {
        s = WallListStore.ExcavOf(db, out _);
        var (tc, tf) = TablesOf(db, s, null);
        string tp = WallExcav.TablesPrint(tc, tf);
        string ph = "", nonce = "";
        try
        {
            var civ = Autodesk.Civil.ApplicationServices.CivilApplication.ActiveDocument;
            using var tr = db.TransactionManager.StartTransaction();
            var (ids, _) = Commands.WallCompositeCommand.Candidates(civ, tr, Commands.WallCompositeCommand.PlanName, groundId);
            if (ids.Count == 1)
            {
                ph = ids[0].Handle.ToString();
                nonce = Commands.WallCompositeCommand.CompositeNonce(tr, tr.GetObject(ids[0], OpenMode.ForRead));
            }
            tr.Commit();
        }
        catch { }
        return HeadOf(s, tp, ph, nonce, groundId.IsNull ? "" : groundId.Handle.ToString());
    }

    /// <summary>★[v105 · 계획 검토 중간 3] 지금 설정 · 표 · 정지면과 같은 도장 머리의 터파기면이 도면에 있나(종단이 지어 둔 것 — 수량 단면 = 지금 단면) · 모르면 null(원지반 없음 · 못 읽음 — ⚠를 띄우지 않는다).</summary>
    internal static bool? HasCurrentSurface(Database db, ObjectId groundId)
    {
        if (groundId.IsNull) return null;
        try
        {
            string head = CurrentHead(db, groundId, out _);
            using var tr = db.TransactionManager.StartTransaction();
            bool ok = Existing(tr).Any(e => !e.Id.IsErased && StampHead(e.Stamp) == head);
            tr.Commit();
            return ok;
        }
        catch { return null; }
    }

    // ══ ★★[v104 · 계획 6] 단면 기록 DH_WALLSEC — 터파기면에 붙인다(횡단 수량의 구조물 공제가 읽는다 · 도장과 함께 바뀐다) ══
    const string SecKey = "DH_WALLSEC";

    static void WriteSecRecord(Transaction tr, DBObject o, WallExcav.SecRecord rec)
    {
        if (!o.IsWriteEnabled) o.UpgradeOpen();
        if (o.ExtensionDictionary.IsNull) o.CreateExtensionDictionary();
        var d = (DBDictionary)tr.GetObject(o.ExtensionDictionary, OpenMode.ForWrite);
        using var rb = WallListStore.BufferOf(WallExcav.EncodeRecord(rec));
        var xr = new Xrecord { Data = rb };
        if (d.Contains(SecKey)) { var old = tr.GetObject(d.GetAt(SecKey), OpenMode.ForWrite); old.Erase(); }
        d.SetAt(SecKey, xr);
        tr.AddNewlyCreatedDBObject(xr, true);
    }

    /// <summary>터파기면의 단면 기록 — 없거나 못 읽으면 null + 까닭(옛 v103.3 면은 기록이 없다 — 구조물 공제 없이 셈).</summary>
    internal static WallExcav.SecRecord? ReadSecRecord(Transaction tr, DBObject o, out string why)
    {
        why = "";
        try
        {
            if (o.ExtensionDictionary.IsNull || tr.GetObject(o.ExtensionDictionary, OpenMode.ForRead) is not DBDictionary d || !d.Contains(SecKey))
            { why = "단면 기록 없음(v104 전에 지은 터파기면 — [종단도]를 다시 그리면 생김)"; return null; }
            if (tr.GetObject(d.GetAt(SecKey), OpenMode.ForRead) is not Xrecord xr) { why = "단면 기록이 XRecord가 아니다"; return null; }
            var vals = WallListStore.ValsOf(xr.Data, out why);
            if (vals == null) return null;
            return WallExcav.TryDecodeRecord(vals, out var rec, out why) ? rec : null;
        }
        catch (System.Exception ex) { why = $"단면 기록을 못 읽었다 {ex.GetType().Name}: {ex.Message}"; return null; }
    }

    static string ReadStamp(Transaction tr, DBObject o, int idx)
    {
        try
        {
            if (o.ExtensionDictionary.IsNull) return "";
            if (tr.GetObject(o.ExtensionDictionary, OpenMode.ForRead) is not DBDictionary d || !d.Contains(StampKey)) return "";
            if (tr.GetObject(d.GetAt(StampKey), OpenMode.ForRead) is not Xrecord xr || xr.Data == null) return "";
            var a = xr.Data.AsArray();
            return a.Length > idx && a[idx].Value is string s ? s : "";
        }
        catch { return ""; }
    }
    static void WriteStamp(Transaction tr, DBObject o, string stamp, string info, string warn)
    {
        if (!o.IsWriteEnabled) o.UpgradeOpen();
        if (o.ExtensionDictionary.IsNull) o.CreateExtensionDictionary();
        var d = (DBDictionary)tr.GetObject(o.ExtensionDictionary, OpenMode.ForWrite);
        var xr = new Xrecord { Data = new ResultBuffer(new TypedValue((int)DxfCode.Text, stamp), new TypedValue((int)DxfCode.Text, info), new TypedValue((int)DxfCode.Text, warn ?? "")) };
        if (d.Contains(StampKey)) { var old = tr.GetObject(d.GetAt(StampKey), OpenMode.ForWrite); old.Erase(); }
        d.SetAt(StampKey, xr);
        tr.AddNewlyCreatedDBObject(xr, true);
    }

    static bool IsOurs(string nm) => nm == Name || (nm.StartsWith(Name + "_") && int.TryParse(nm.Substring(Name.Length + 1), out _));
    /// <summary>이름이 우리 터파기면 · 임시 · 조각인가 — <c>FindSurfaces</c>가 이름으로 줍지 않게 명시해 건너뛴다(재검토 중간 6).</summary>
    internal static bool IsAnyOurs(string nm) => IsOurs(nm) || nm == TempName || nm == PieceName || (nm.StartsWith(PieceName + "_") && int.TryParse(nm.Substring(PieceName.Length + 1), out _))
                                                 || WallBackBuilder.IsAnyOurs(nm);   // ★[v104 B] 배면 면도 이름으로 안 줍는다

    static System.Collections.Generic.List<(ObjectId Id, string Name, string Stamp)> Existing(Transaction tr)
    {
        var civ = Autodesk.Civil.ApplicationServices.CivilApplication.ActiveDocument;
        var res = new System.Collections.Generic.List<(ObjectId, string, string)>();
        foreach (ObjectId sid in civ.GetSurfaceIds())
        {
            try
            {
                if (tr.GetObject(sid, OpenMode.ForRead) is not Surface s) continue;
                string nm = s.Name ?? "";
                if (IsOurs(nm)) res.Add((sid, nm, ReadStamp(tr, s, 0)));
            }
            catch { }
        }
        return res;
    }

    // ══ 선형 기록 ══════════════════════════════════════════════════════════

    internal static void RecordOnAlignment(Transaction tr, ObjectId alignId, Result r)
    {
        var al = tr.GetObject(alignId, OpenMode.ForWrite);
        if (al.ExtensionDictionary.IsNull) al.CreateExtensionDictionary();
        var d = (DBDictionary)tr.GetObject(al.ExtensionDictionary, OpenMode.ForWrite);
        var xr = new Xrecord
        {
            Data = new ResultBuffer(
                new TypedValue((int)DxfCode.Text, r.State == State.Built && !r.SurfaceId.IsNull ? r.SurfaceId.Handle.ToString() : ""),
                new TypedValue((int)DxfCode.Text, r.Stamp ?? ""),
                new TypedValue((int)DxfCode.Int32, (int)r.State),
                new TypedValue((int)DxfCode.Text, r.Why ?? ""),
                // ★[v104 B] 배면 — 면 핸들 · 상태 · 까닭 · 지을 때 배면 설정 머리(횡단이 견줌)
                new TypedValue((int)DxfCode.Text, r.BackState == State.Built && !r.BackId.IsNull ? r.BackId.Handle.ToString() : ""),
                new TypedValue((int)DxfCode.Int32, (int)r.BackState),
                new TypedValue((int)DxfCode.Text, r.BackWhy ?? ""),
                new TypedValue((int)DxfCode.Text, r.BackHead ?? ""))
        };
        if (d.Contains(AlignKey)) { var old = tr.GetObject(d.GetAt(AlignKey), OpenMode.ForWrite); old.Erase(); }
        d.SetAt(AlignKey, xr);
        tr.AddNewlyCreatedDBObject(xr, true);
    }

    internal static (string Handle, string Stamp, State State, string Why)? ReadAlign(Transaction tr, DBObject al)
    {
        try
        {
            if (al.ExtensionDictionary.IsNull) return null;
            if (tr.GetObject(al.ExtensionDictionary, OpenMode.ForRead) is not DBDictionary d || !d.Contains(AlignKey)) return null;
            if (tr.GetObject(d.GetAt(AlignKey), OpenMode.ForRead) is not Xrecord xr || xr.Data == null) return null;
            var a = xr.Data.AsArray();
            if (a.Length < 4) return null;
            return (a[0].Value as string ?? "", a[1].Value as string ?? "", a[2].Value is int k ? (State)k : State.Failed, a[3].Value as string ?? "");
        }
        catch { return null; }
    }

    /// <summary>★[v104 B] 선형 기록의 배면 칸 — 판 1(v103.3 · v104 A) 기록은 칸이 없어 null.</summary>
    internal static (string Handle, State State, string Why, string Head)? ReadAlignBack(Transaction tr, DBObject al)
    {
        try
        {
            if (al.ExtensionDictionary.IsNull) return null;
            if (tr.GetObject(al.ExtensionDictionary, OpenMode.ForRead) is not DBDictionary d || !d.Contains(AlignKey)) return null;
            if (tr.GetObject(d.GetAt(AlignKey), OpenMode.ForRead) is not Xrecord xr || xr.Data == null) return null;
            var a = xr.Data.AsArray();
            if (a.Length < 8) return null;
            return (a[4].Value as string ?? "", a[5].Value is int k ? (State)k : State.Failed, a[6].Value as string ?? "", a[7].Value as string ?? "");
        }
        catch { return null; }
    }

    /// <summary>어느 선형도 안 쓰는 옛 터파기면을 지우고, 본 이름이 비면 지금 면에 돌려준다(옹벽면 <c>Tidy</c>와 같은 법). ★[v104 B] 배면 면도 같이.</summary>
    internal static string Tidy(Transaction tr, ObjectId keep, ObjectId keepBack = default)
    {
        var refd = new System.Collections.Generic.HashSet<string>();
        var refdB = new System.Collections.Generic.HashSet<string>();
        try
        {
            var civ = Autodesk.Civil.ApplicationServices.CivilApplication.ActiveDocument;
            foreach (ObjectId aid in civ.GetAlignmentIds())
                try
                {
                    var al = tr.GetObject(aid, OpenMode.ForRead);
                    var r = ReadAlign(tr, al); if (r != null && r.Value.Handle.Length > 0) refd.Add(r.Value.Handle);
                    var rb = ReadAlignBack(tr, al); if (rb != null && rb.Value.Handle.Length > 0) refdB.Add(rb.Value.Handle);
                }
                catch { }
        }
        catch { }
        string tidyB = WallBackBuilder.Tidy(tr, keepBack, refdB);
        int erased = 0; var names = new System.Collections.Generic.List<string>();
        foreach (var e in Existing(tr))
        {
            if (e.Id == keep || e.Id.IsErased || refd.Contains(e.Id.Handle.ToString())) continue;
            try { tr.GetObject(e.Id, OpenMode.ForWrite).Erase(); erased++; names.Add(e.Name); } catch { }
        }
        string renamed = "";
        try
        {
            if (!keep.IsNull && !keep.IsErased && tr.GetObject(keep, OpenMode.ForRead) is Surface s && (s.Name ?? "").StartsWith(Name + "_", System.StringComparison.Ordinal)
                && !Existing(tr).Any(e => e.Id != keep && !e.Id.IsErased && e.Name == Name))
            { s.UpgradeOpen(); renamed = s.Name + " → " + Name; s.Name = Name; }
        }
        catch { renamed = ""; }
        string tidyX = erased == 0 && renamed.Length == 0 ? "" : $"옹벽 터파기면 정리 — 아무 선형도 안 쓰는 옛 면 {erased}개 지움{(names.Count > 0 ? "(" + string.Join(",", names) + ")" : "")}{(renamed.Length > 0 ? " · 이름 " + renamed : "")}";
        return string.Join(" · ", new[] { tidyX, tidyB }.Where(x => x.Length > 0));
    }

    /// <summary>종단이 쓸 목록 맨 뒤에 붙인다(밴드 순서를 안 흔든다 — FindSurfaces 주석).</summary>
    internal static void AppendPick(System.Collections.Generic.List<Commands.SectionCommand.SurfPick> surfs, Result? r)
    {
        if (r == null || r.State != State.Built || r.SurfaceId.IsNull) return;
        surfs.RemoveAll(sp => sp.Label == Label || sp.Label == WallBackBuilder.Label);
        surfs.Add(new Commands.SectionCommand.SurfPick(r.SurfaceId, r.SurfaceName, ProfName, Label));
        if (r.BackState == State.Built && !r.BackId.IsNull) surfs.Add(new Commands.SectionCommand.SurfPick(r.BackId, r.BackName, WallBackBuilder.ProfName, WallBackBuilder.Label));   // ★[v104 B]
    }

    /// <summary>★[계획 2-4 · 재검토 중간 5 · 6] 횡단 · 검토선 — 선형 기록을 따라 터파기면을 고른다. 거짓 = 멈춤(«종단 먼저»).</summary>
    internal static bool ForAlignment(Database db, ObjectId alignId, ObjectId groundId, System.Collections.Generic.List<Commands.SectionCommand.SurfPick> surfs,
                                      System.Action<string> say)
    {
        string now = CurrentHead(db, groundId, out var s);
        try
        {
            using var tr = db.TransactionManager.StartTransaction();
            var al = tr.GetObject(alignId, OpenMode.ForRead);
            var rec = ReadAlign(tr, al);
            var recB = ReadAlignBack(tr, al);
            bool ourProfile = PureWithWallsBuilder.ReadAlign(tr, al) != null;
            tr.Commit();
            if (rec == null)
            {
                if (!s.On) return true;
                if (ourProfile) { say("옹벽 터파기를 켠 뒤 종단을 다시 안 그렸습니다 — [종단도]를 먼저 다시 그리세요."); return false; }
                say("⚠옹벽 터파기 빠짐 — 이 노선은 터파기 기록이 없습니다([종단도]를 그리면 들어갑니다).");
                return true;
            }
            var r = rec.Value;
            if (r.State == State.Off)
            {
                if (s.On) { say("옹벽 터파기를 켠 뒤 종단을 다시 안 그렸습니다 — [종단도]를 먼저 다시 그리세요."); return false; }
                return true;
            }
            if (!s.On) { say("옹벽 터파기를 끈 뒤 종단을 다시 안 그렸습니다 — [종단도]를 먼저 다시 그리세요."); return false; }
            if (StampHead(r.Stamp) != now) { say("옹벽 터파기 설정 · 정지면이 바뀌었습니다 — [종단도]를 먼저 다시 그리세요."); return false; }
            if (r.State == State.Failed) { say("⚠종단이 옹벽 터파기면을 못 지었습니다 — " + r.Why); return true; }
            ObjectId sid = ObjectId.Null;
            try { sid = db.GetObjectId(false, new Handle(System.Convert.ToInt64(r.Handle, 16)), 0); } catch { }
            string nm = "";
            bool alive = false;
            using (var tr2 = db.TransactionManager.StartTransaction())
            {
                try { alive = !sid.IsNull && !sid.IsErased && tr2.GetObject(sid, OpenMode.ForRead) is Surface s1 && (nm = s1.Name ?? "") != null; } catch { }
                tr2.Commit();
            }
            if (!alive) { say("종단이 쓴 옹벽 터파기면이 없습니다 — [종단도]를 다시 그리세요."); return false; }
            // ★[v104 B] 배면 — 기록이 없거나(옛 기록) 지을 때 배면 설정이 지금과 다르면 «종단 먼저»
            var res0 = new Result { State = State.Built, SurfaceId = sid, SurfaceName = nm };
            // ★[v104 설치 전 검토 높음 1] 머리를 «끔이면 끔 하나로» 맞춰 견주고, 다르면 상태와 상관없이 멈춘다(종전엔 못 지음 → 끔으로 바꾸면 통과해 옛 기록으로 쟀다)
            // ★[v104.1 설치 전 검토 낮음 10] 배면 머리는 «1|n» · «0|n» 꼴 — 종전엔 «False»만 끔으로 봐서 끔 상태에서 경사만 바꿔도 · 배면 칸 없는 옛 기록이어도 «종단 먼저»로 막았다
            static string BackNorm(string h) => h.StartsWith("False") || h.StartsWith("0|") ? "off" : h;
            if (BackNorm(recB?.Head ?? "False") != BackNorm(s.BackHead)) { say("옹벽 배면사면 설정이 바뀌었습니다 — [종단도]를 먼저 다시 그리세요."); return false; }
            // ★[검토 높음 1] 수량 기록은 터파기면 하나에 붙는다 — 같은 면을 쓰는 다른 선형이 다른 경사로 그려 덮었으면 멈춘다
            if (s.BackOn)
            {
                string bw = ""; WallExcav.BackRecord? xb = null;
                using (var tr4 = db.TransactionManager.StartTransaction())
                {
                    try { xb = WallBackBuilder.ReadRecord(tr4, tr4.GetObject(sid, OpenMode.ForRead), out bw); } catch { }
                    tr4.Commit();
                }
                // ★[v104.1 · 재검토 낮음 · 설치 전 검토 중간 2] 배면 기록 판 3(평탄 높이 식이 바뀜) — 기록이 있는데 못 읽으면(옛 판 · 깨짐) 상태와 상관없이 멈춘다
                //   (못 지음 · 땅과 같음 상태에도 기록은 남는다 — 종전엔 «지음»만 봐서 수량이 말없이 배면 없이 나왔다) · 기록이 아예 없으면 아래 상태가 말한다
                if (xb == null && bw != "배면 기록 없음")
                {
                    say($"옹벽 배면 기록을 이 판에서 못 씁니다({bw}) — [종단도]를 먼저 다시 그리세요.");
                    return false;
                }
                if (xb != null && System.Math.Abs(xb.N - s.BackSlope) > 1e-9)
                {
                    say($"옹벽 터파기면의 배면 기록(1:{xb.N:0.##})이 지금 설정(1:{s.BackSlope:0.##})과 다릅니다(같은 면을 쓰는 다른 노선을 다른 경사로 그림) — [종단도]를 먼저 다시 그리세요.");
                    return false;
                }
            }
            if (recB != null && s.BackOn && recB.Value.State == State.Built)
            {
                ObjectId bid = ObjectId.Null; string bnm = ""; bool balive = false;
                try { bid = db.GetObjectId(false, new Handle(System.Convert.ToInt64(recB.Value.Handle, 16)), 0); } catch { }
                using (var tr3 = db.TransactionManager.StartTransaction())
                {
                    try { balive = !bid.IsNull && !bid.IsErased && tr3.GetObject(bid, OpenMode.ForRead) is Surface sb && (bnm = sb.Name ?? "") != null; } catch { }
                    tr3.Commit();
                }
                if (!balive) { say("종단이 쓴 옹벽 배면 면이 없습니다 — [종단도]를 다시 그리세요."); return false; }
                res0.BackState = State.Built; res0.BackId = bid; res0.BackName = bnm;
            }
            else if (recB != null && s.BackOn && recB.Value.State == State.Failed) say("⚠종단이 옹벽 배면 면을 못 지었습니다 — " + recB.Value.Why);
            AppendPick(surfs, res0);
            if (r.Why.Length > 0) say("⚠종단이 쓴 옹벽 터파기면 — " + r.Why);
            return true;
        }
        catch (System.Exception ex) { say("⚠옹벽 터파기 기록을 못 읽었다 — 터파기 없이 그립니다: " + ex.Message); return true; }
    }

    // ══ 짓기 ════════════════════════════════════════════════════════════════

    /// <summary>★계획 2-3 · 2-4 — 터파기면을 짓거나 되쓴다(꺼짐이면 아무것도 안 함). 예외를 안 던진다(못 지으면 «못 지음» + 까닭).</summary>
    /// <param name="pure">이번 종단의 옹벽면 결과 — 그 <c>Walls</c>가 종단 정지면에 든 구간 · 굳은 옹벽이다(재검토 중간 7).</param>
    internal static Result Ensure(Database db, ObjectId groundId, PureWithWallsBuilder.Result? pure, StringBuilder log)
    {
        var evPrev = EventViewerMute.Begin();   // LandXML 가져오기 정보 한 줄에 이벤트 뷰어 창이 안 뜨게(v103.2.1과 같은 법)
        try { return EnsureCore(db, groundId, pure, log); }
        finally { EventViewerMute.End(evPrev); }
    }

    sealed class PieceWork
    {
        public WallExcav.Bottom Bottom = null!;
        public WallExcav.SecLine Sec = null!;
        public System.Collections.Generic.List<Point3> Ring = new();
        public double Rise, ZbMin, DepthMax, Reach;
        public bool Benches, Grown;
        public WallDaylight.ExcavPiece? Piece;
        /// <summary>★[v104] Civil이 지은 조각 삼각형(도랑 자르기 전) · 그 바깥 링 — 도랑 자르기는 모든 조각이 지어진 뒤 <c>ApplyClips</c>가 한다.</summary>
        public System.Collections.Generic.List<Tri>? Raw;
        public System.Collections.Generic.List<Point3> Outer = new();
        public string ClipNote = "";
        public string Note = "";
        /// <summary>★[v105.3] 참 원뿔로 낮춘 꼭짓점 수 · 가장 크게 낮춘 높이(벽 끝 절벽 고침).</summary>
        public int Low;
        public double LowMax;
    }

    /// <summary>★★[v105 · 계획_v105.md 4.1 · JACK 1022 «3D는 터파기와 같은 단면»] 앞면 선 → 바닥 줄 — 구간 · 굳은 옹벽(<paramref name="walls"/> = 종단 정지면에 든 것) · 번들 옹벽.
    /// 터파기(<see cref="EnsureCore"/>)와 3D 내보내기(<see cref="WallSolidDwg"/>)가 같이 쓴다. 벽 높이 · 벽 밑은 <paramref name="mAt"/>(끊김 없는 정지면_DH)에서 잰다.</summary>
    internal static (System.Collections.Generic.List<WallExcav.Bottom> Bottoms, System.Collections.Generic.Dictionary<WallExcav.Bottom, double> BenchBelow, int Faces,
                     System.Collections.Generic.List<GradingBundle>? Bundles)
        CollectBottoms(Database db, Transaction tr, System.Collections.Generic.IEnumerable<PureWithWallsBuilder.ReflWall>? walls, System.Func<double, double, double?> mAt,
                       System.Collections.Generic.List<string> notes, WallExcav.MeasureNote mn, StringBuilder fp, System.Collections.Generic.List<string> stepLog)
    {
        var bottoms = new System.Collections.Generic.List<WallExcav.Bottom>();
        var benchBelow = new System.Collections.Generic.Dictionary<WallExcav.Bottom, double>();
        int nFaces = 0;
        double gsMin = GradingSettings.MinSlope, gsRun = GradingSettings.MinFaceRun;
        foreach (var w in walls ?? System.Linq.Enumerable.Empty<PureWithWallsBuilder.ReflWall>())
        {
            string src = (w.Frozen ? "굳은 옹벽 " : "옹벽 ") + w.No;
            if (w.IsWall.Count != w.Poly.Count || w.Poly.Count < 3)
            {
                notes.Add(w.Frozen ? $"옛 굳은 옹벽 {w.No} — 옹벽선 기록이 없어 터파기에서 빠짐(앞 구역을 다시 생성하면 들어감)" : $"{src} — 옹벽선 표가 없어 빠짐");
                continue;
            }
            double slopeW = w.SlopeW >= 0 ? w.SlopeW : gsMin, faceRun = w.FaceRun >= 0 ? w.FaceRun : gsRun;
            // ★★[설치 전 검토 높음 1] 단 수는 벽 짓기와 같은 셈(폴리곤 안 최종면 최고 · 최저로 BenchCount)으로 묶는다 — «벽이 안 보일 때까지»로 세면
            //   비탈진 땅을 단으로 잡아 가짜 단이 이어질 수 있었다. 한 단 더(N+1)는 재기만 해서 벽이 1 m 넘게 보이면 ⚠(넣지 않는다).
            int nB = WallExcav.ZoneBenches(w.Poly, w.Up, w.HeadZ, w.H, mAt, System.Math.Max(1.0, GradingSettings.VertexSpacing));
            if (nB <= 0) { notes.Add($"{src} — 폴리곤 안 최종면을 못 재 단 수를 모른다(1단만)"); nB = 1; }
            int kFound = 0;
            for (int k = 1; k <= nB + 1; k++)
            {
                var faces = WallExcav.ZoneFaces(w.Poly, w.IsWall, w.Up, w.HeadZ, k, w.H, w.T, slopeW, faceRun, src, out string fn, fromBench: k);
                // ★[재검토 낮음 3] 2단부터도 «못 뽑았다 · 흙 쪽을 못 가렸다»는 알린다(빠진 단이 말없이 사라지지 않게) — 폴리곤 밖은 좁은 자리라 1단만
                if (fn.Length > 0 && k <= nB && (k == 1 || !fn.Contains("앞면 선 없음"))) notes.Add(fn);
                if (k == nB + 1)
                {
                    var mx = new WallExcav.MeasureNote();
                    double keptX = 0;
                    foreach (var f in faces) { WallExcav.Measure(f, mAt, 0, mx); keptX = mx.Kept; }
                    if (keptX > 1.0) notes.Add($"{src} — {nB}단 너머에도 벽이 {keptX:F1}m 보인다(단 수가 모자람 — 그 단은 터파기에 안 넣었다)");
                    break;
                }
                double keptBefore = mn.Kept;
                foreach (var f in faces)
                {
                    nFaces++;
                    fp.Append(f.Src).Append(f.Soil).Append(';');
                    foreach (var q in f.Pts) fp.Append(q.X.ToString("R", System.Globalization.CultureInfo.InvariantCulture)).Append(',').Append(q.Y.ToString("R", System.Globalization.CultureInfo.InvariantCulture)).Append(';');
                    foreach (var b0 in WallExcav.Measure(f, mAt, 0, mn))
                    {
                        b0.Up = w.Up; b0.Group = src; b0.Step = k;
                        // ★[v104 계획 5.7] 아래에 단이 있는 단 — 그 사이 소단 폭(위 단 앞 터파기가 넘으면 ⚠)
                        if (w.Up ? k >= 2 : k < nB) benchBelow[b0] = w.T;
                        bottoms.Add(b0);
                    }
                }
                if (mn.Kept - keptBefore > 1e-6) kFound = k;
            }
            stepLog.Add($"{src} {(w.Up ? "절토" : "성토")} {kFound}단(셈 {nB}단)");
            // ★[v104 B] 맨 위 단(찾은 가장 위 단) — 배면사면 대상(절토만)
            if (w.Up) foreach (var bb in bottoms) if (bb.Group == src && bb.Step == kFound) bb.Top = true;
        }
        var bundles = GradingBundleStore.TryLoadAll(db, tr, out string bwhy);
        if (bundles != null)
        {
            int zi = 0;
            foreach (var b in bundles)
            {
                zi++;
                foreach (var (runs, up) in new[] { (b.CutWallRuns, true), (b.FillWallRuns, false) })
                {
                    if (runs == null) continue;
                    int ri = 0;
                    foreach (var r in runs)
                    {
                        ri++;
                        string src = $"구역{zi} {(up ? "절토" : "성토")} 옹벽선{ri}({r.Bench + 1}단)";
                        var f = WallExcav.RunFace(r, b.Params.MinSlope, b.Params.MinFaceRun, src, out string why);
                        if (f == null) { notes.Add(why); continue; }
                        nFaces++;
                        fp.Append(f.Src).Append(f.Soil).Append(';');
                        foreach (var q in f.Pts) fp.Append(q.X.ToString("R", System.Globalization.CultureInfo.InvariantCulture)).Append(',').Append(q.Y.ToString("R", System.Globalization.CultureInfo.InvariantCulture)).Append(';');
                        foreach (var b0 in WallExcav.Measure(f, mAt, 0, mn))
                        {
                            b0.Up = up; b0.Group = $"구역{zi}"; b0.Step = r.Bench;
                            if (up && r.Bench >= 1) benchBelow[b0] = b.Params.CutBenchWidth;
                            bottoms.Add(b0);
                        }
                    }
                }
            }
        }
        else if (bwhy.Length > 0) notes.Add("번들을 못 읽어 번들 옹벽은 빠짐 — " + bwhy);
        // ★[v104 B] 번들 — 구역마다 절토 런 중 가장 높은 단이 맨 위
        foreach (var g in bottoms.Where(x => x.Up && x.Group.StartsWith("구역")).GroupBy(x => x.Group))
        {
            int top = g.Max(x => x.Step);
            foreach (var bb in g) if (bb.Step == top) bb.Top = true;
        }
        return (bottoms, benchBelow, nFaces, bundles);
    }

    /// <summary>★★[v105 · 계획 4.1] 바닥 줄마다 형식 표 → 단면(<see cref="WallExcav.Sectionize"/>) — 터파기 · 3D가 같이 쓴다. 못 세운 줄 · 앞굽 ⚠는 <paramref name="notes"/>에.</summary>
    internal static System.Collections.Generic.List<(WallExcav.Bottom B, WallExcav.SecLine S)> SectionizeAll(System.Collections.Generic.List<WallExcav.Bottom> bottoms,
        System.Collections.Generic.Dictionary<WallExcav.Bottom, double> benchBelow, WallSection.Table tCut, WallSection.Table tFill, WallExcav.Settings st,
        System.Collections.Generic.List<string> notes, WallExcav.SecNote secNote)
    {
        var secs = new System.Collections.Generic.List<(WallExcav.Bottom B, WallExcav.SecLine S)>();
        foreach (var b in bottoms)
        {
            var sl = WallExcav.Sectionize(b, b.Up ? tCut : tFill, st.Common(b.SlopeW), st.Margin, secNote, out string sw0);
            if (sl == null) { notes.Add($"{b.Src} 단면을 못 세움 — {sw0}"); continue; }
            secs.Add((b, sl));
            if (benchBelow.TryGetValue(b, out double bw) && sl.FrontMax > bw + 1e-6)
            {
                notes.Add($"{b.Src} — 앞 터파기 폭 {sl.FrontMax:F2}m가 아래 단 소단 폭 {bw:F2}m를 넘음(아래 단 벽 위까지 판다 · 터파기는 하나로 합쳐짐)");
                // ★[v104.0.1 · JACK 1022 «옹벽 모양이 소단 밖으로 튀어나와»] 단면(저판) 자체가 소단보다 길면 위 단 저판이 아래 단 벽 앞 허공으로 나온다 — 모양은 그대로 · ⚠만
                double toe = sl.Pts.Count == 0 ? 0 : sl.Pts.Max(p => -p.Shape.XFront);
                if (toe > bw + 1e-6) notes.Add($"{b.Src} — ⚠앞굽 {toe:F2}m가 아래 단 소단 {bw:F2}m보다 길다(위 단 저판이 아래 단 벽 앞으로 나온다 — 소단을 넓히거나 형식을 바꿀 것)");
            }
        }
        return secs;
    }

    /// <summary>★[v105 · 계획 4.1] 3D 내보내기용 — 도면을 바꾸지 않고(읽기만) 지금 단면 줄을 세운다: 정지면_DH · 종단에 든 옹벽(<see cref="PureWithWallsBuilder.CurrentWalls"/>) · 번들 옹벽 · 이 도면 단면 표.
    /// 터파기가 꺼져 있어도 쓴다(설정 기록의 단면 형식 · 표 · 버림 칸). 수량 단면과 같은지는 부르는 쪽이 <see cref="HasCurrentSurface"/>로 본다.</summary>
    /// <param name="cover">★[v106 · 계획 2판 2.3] 지형 벽면 자가점검(정지면_DH가 3D 앞면보다 1 cm 넘게 앞인 표본 — 전단 뒤) · 단면 줄을 못 세우면 빈 셈.</param>
    internal static System.Collections.Generic.List<WallExcav.SecLine>? SectionLinesForExport(Database db, ObjectId groundId, System.Collections.Generic.List<string> notes, out WallExcav.Settings st, out string facesTxt, out WallSweep.CoverNote cover)
    {
        st = WallListStore.ExcavOf(db, out string swhy);
        facesTxt = "";
        cover = new WallSweep.CoverNote();
        if (swhy.Length > 0) notes.Add("옹벽 터파기 설정 기록 — " + swhy);
        var (tCut, tFill) = TablesOf(db, st, notes);
        var civ = Autodesk.Civil.ApplicationServices.CivilApplication.ActiveDocument;
        using var tr = db.TransactionManager.StartTransaction();
        var (planIds, planNms) = Commands.WallCompositeCommand.Candidates(civ, tr, Commands.WallCompositeCommand.PlanName, groundId);
        if (planIds.Count != 1) { notes.Add($"정지면_DH를 하나로 못 잡았다({(planIds.Count == 0 ? "없음" : string.Join(",", planNms))}) — 옹벽 3D 없음"); tr.Commit(); return null; }
        var sC = new CachedGroundSurface((TinSurface)tr.GetObject(planIds[0], OpenMode.ForRead));
        System.Func<double, double, double?> mAt = (x, y) => sC.TryGetElevation(x, y, out double z) ? z : (double?)null;
        var walls = PureWithWallsBuilder.CurrentWalls(db, tr, groundId, out _);
        var mn = new WallExcav.MeasureNote();
        var stepLog = new System.Collections.Generic.List<string>();
        var (bottoms, benchBelow, nFaces, bundles) = CollectBottoms(db, tr, walls, mAt, notes, mn, new StringBuilder(), stepLog);
        var secNote = new WallExcav.SecNote();
        var secs = SectionizeAll(bottoms, benchBelow, tCut, tFill, st, notes, secNote);
        if (secNote.Warn) notes.Add("단면 — " + secNote.Text);
        facesTxt = $"앞면 선 {nFaces}줄({string.Join(" · ", stepLog)}{(bundles != null ? $" · 번들 구역 {bundles.Count}" : "")}) · 바닥 줄 {bottoms.Count} · 단면 줄 {secs.Count}";
        // ★[v106 · 계획 2판 2.3] 지형 벽면 자가점검 — 3D 앞면(전단 뒤)이 정지면_DH 벽면보다 앞에 서는가(InfraWorks에서 지형이 무늬를 안 가리나)
        try { foreach (var (_, sl) in secs) WallSweep.CoverCheck(sl, mAt, cover); }
        catch (System.Exception cex) { notes.Add("지형 벽면 자가점검을 못 함 — " + cex.Message); }   // ★[검토 낮음 L4] 자가점검 탓에 3D를 잃지 않게
        tr.Commit();
        return secs.Select(x => x.S).ToList();
    }

    private static Result EnsureCore(Database db, ObjectId groundId, PureWithWallsBuilder.Result? pure, StringBuilder log)
    {
        var R = new Result();
        var sw = System.Diagnostics.Stopwatch.StartNew();
        var civ = Autodesk.Civil.ApplicationServices.CivilApplication.ActiveDocument;
        var st = WallListStore.ExcavOf(db, out string swhy);
        R.Settings = st;
        if (swhy.Length > 0) log.AppendLine("  옹벽 터파기 — ⚠설정 기록: " + swhy);
        if (!st.On)
        {
            R.State = State.Off;
            R.Stamp = HeadOf(st, "", "", "", "");
            log.AppendLine("  옹벽 터파기 — 꺼짐");
            return R;
        }
        ObjectId made = ObjectId.Null;
        var pieceIds = new System.Collections.Generic.List<ObjectId>();
        var notes = new System.Collections.Generic.List<string>();
        // ★★[v104 · 계획 4 · 5.6] 형식별 높이 표(도면 기록) — 지문을 도장 머리에(표를 고치면 «종단 먼저»)
        var (tCut, tFill) = TablesOf(db, st, notes);
        string tp = WallExcav.TablesPrint(tCut, tFill);
        string failLog = "";   // ★[v103.3.1] 못 했을 때 로그에 덧붙일 앞면 · 조각 · 입력 남긴 자리
        try
        {
            using var tr = db.TransactionManager.StartTransaction();
            // 0. 지난번에 남은 임시 · 조각 면
            int temp = 0;
            foreach (ObjectId sid in civ.GetSurfaceIds())
                try { if (tr.GetObject(sid, OpenMode.ForRead) is Surface s && (s.Name == TempName || s.Name == PieceName || (s.Name ?? "").StartsWith(PieceName + "_"))) { tr.GetObject(sid, OpenMode.ForWrite).Erase(); temp++; } } catch { }
            if (temp > 0) log.AppendLine($"  옹벽 터파기 — 지난번 임시 면 {temp}개 지움");
            // 1. 면 — 정지면_DH(기준면 · 재는 면 S) · 원지반(G)
            var (planIds, planNms) = Commands.WallCompositeCommand.Candidates(civ, tr, Commands.WallCompositeCommand.PlanName, groundId);
            if (planIds.Count != 1) return Fail($"정지면_DH를 하나로 못 잡았다({(planIds.Count == 0 ? "없음" : string.Join(",", planNms))})");
            if (groundId.IsNull || groundId.IsErased || tr.GetObject(groundId, OpenMode.ForRead) is not TinSurface gTin) return Fail("원지반이 없다");
            var sTin = (TinSurface)tr.GetObject(planIds[0], OpenMode.ForRead);
            string nonce = Commands.WallCompositeCommand.CompositeNonce(tr, sTin);
            string head = HeadOf(st, tp, planIds[0].Handle.ToString(), nonce, groundId.Handle.ToString());
            R.Stamp = head + "#";   // 아래에서 못 지어도 머리는 맞게(횡단이 «바뀜»을 헛되게 말하지 않게)
            var sC = new CachedGroundSurface(sTin);
            var gC = new CachedGroundSurface(gTin);

            // 2. 앞면 선 — 구간 · 굳은 옹벽(종단 정지면에 든 것 = pure.Walls) · 번들 옹벽
            //   ★[1021 화면 없는 Civil 시험 — 12/22 실패의 뿌리] 벽 높이 · 벽 밑은 <b>끊김 없는 정지면_DH</b>에서 잰다 — 종단 옹벽면(정지순수옹벽_DH)에는
            //     벽 위 원지반(뚜껑) · 성토 옹벽 앞 원지반이 빠져 흙 쪽 표본이 전부 «면을 못 읽음»이었다. 옹벽면은 정지면_DH 삼각형을 그대로 골라 낸 것이라 값이 같다.
            long t0 = sw.ElapsedMilliseconds;
            System.Func<double, double, double?> mAt = (x, y) => sC.TryGetElevation(x, y, out double z) ? z : (double?)null;
            var mn = new WallExcav.MeasureNote();
            var fp = new StringBuilder();       // 앞면 지문
            var stepLog = new System.Collections.Generic.List<string>();
            // ★[v105 · 계획 4.1] 3D 내보내기(WallSolidDwg)와 같은 함수 — 터파기와 3D가 같은 바닥 줄 · 같은 단면
            var (bottoms, benchBelow, nFaces, bundles) = CollectBottoms(db, tr, pure?.Walls, mAt, notes, mn, fp, stepLog);
            long tFaces = sw.ElapsedMilliseconds - t0;
            if (mn.Warn) notes.Add("앞면 재기 — " + mn.Text);
            R.Faces = nFaces;
            // ★[설치 전 검토 낮음 5] 지문에 정지면 · 원지반 삼각형 지문도 — 같은 핸들로 면을 그 자리에서 고쳐도 옛 터파기면을 되쓰지 않게
            fp.Append("|S").Append(sC.Signature).Append("|G").Append(gC.Signature);
            string fpHash;
            using (var sha = System.Security.Cryptography.SHA256.Create())
                fpHash = System.Convert.ToHexString(sha.ComputeHash(Encoding.UTF8.GetBytes(fp.ToString()))).Substring(0, 16);
            R.Stamp = head + "#" + fpHash + "#v4";   // ★[v105] 3 = 단면 기록 판 3(표 칸 · 조건) · ★[v105.3] 4 = 조각 원뿔 낮추기(옛 절벽 면을 되쓰지 않게)
            string facesTxt = $"앞면 선 {nFaces}줄({string.Join(" · ", stepLog)}{(bundles != null ? $" · 번들 구역 {bundles.Count}" : "")}) · {mn.Text} · 바닥 줄 {bottoms.Count}";
            if (bottoms.Count == 0)
            {
                R.State = State.Failed; R.Why = nFaces == 0 ? "종단 정지면에 옹벽이 없다" : "옹벽 앞면에서 벽을 못 찾았다(벽 높이 0.1m 미만)";
                if (notes.Count > 0) R.Why += " · " + string.Join(" · ", notes);
                log.AppendLine($"  옹벽 터파기 — {R.StatusText} · {facesTxt}");
                tr.Commit(); return R;
            }

            // 3. 같은 도장의 면이 있으면 되쓴다
            var ex = Existing(tr);
            var same = ex.FirstOrDefault(e => e.Stamp == R.Stamp && !e.Id.IsErased);
            if (!same.Id.IsNull)
            {
                R.State = State.Built; R.SurfaceId = same.Id; R.SurfaceName = same.Name; R.Reused = true;
                try { R.Why = ReadStamp(tr, tr.GetObject(same.Id, OpenMode.ForRead), 2); } catch { }
                WallBackBuilder.Ensure(db, tr, groundId, R, st, sC, gC, log);   // ★[v104 B]
                log.AppendLine($"  옹벽 터파기 — {R.StatusText} · {facesTxt} · {sw.ElapsedMilliseconds}ms");
                tr.Commit(); return R;
            }

            // 4. 조각 — 바닥 띠 → Build → Civil TIN → 삼각형 읽고 지움
            double? L(double x, double y)
            {
                bool hs = sC.TryGetElevation(x, y, out double zs), hg = gC.TryGetElevation(x, y, out double zg);
                return hs && hg ? System.Math.Min(zs, zg) : hs ? zs : hg ? zg : (double?)null;
            }
            long t1 = sw.ElapsedMilliseconds;
            // ★★★[v104 · 계획_v104.md 5.2 · 5.3 · JACK 1021 «옹벽 형식별 일반단면 → 여유폭 · 구배로 터파기»] 바닥 줄 점마다 형식 표 → 단면 →
            //   바닥 높이(앞 땅 + 단면 바닥) · 같은 표 줄 구간 · 구간마다 폭(뒤 끝 + 여유 · −앞 끝 + 여유 · 패널식은 앞면 선 앞 도랑)
            var secNote = new WallExcav.SecNote();
            var secs = SectionizeAll(bottoms, benchBelow, tCut, tFill, st, notes, secNote);
            if (secNote.Warn) notes.Add("단면 — " + secNote.Text);
            string secTxt = "단면 " + string.Join(" · ", secs.GroupBy(x => (x.S.Up, x.S.Kind)).Select(g =>
                $"{(g.Key.Up ? "절토" : "성토")} {g.Key.Kind} {g.Count()}줄·구간 {g.Sum(x => x.S.Runs.Count)}"
                + (g.First().S.Trench ? $"·도랑(기초부만) 앞 {g.Max(x => x.S.FrontMax):F2}m" : $"·뒤 {g.Min(x => x.S.Runs.Min(r => r.Back)):F2}~{g.Max(x => x.S.BackMax):F2}m·앞 {g.Max(x => x.S.FrontMax):F2}m")
                + $"·바닥 깊이 ≤{g.Max(x => x.S.DepthMax):F2}m"));
            var works = new System.Collections.Generic.List<PieceWork>();
            foreach (var (b, sl) in secs)
            {
                var ring = WallExcav.StripRing(b, sl, out string rw);
                if (ring == null) { notes.Add($"{b.Src} 띠 못 만듦 — {rw}"); continue; }
                if (rw.Length > 0) notes.Add($"{b.Src} — {rw}");
                var pw = new PieceWork { Bottom = b, Sec = sl, Ring = ring, ZbMin = ring.Min(q => q.Z) };
                // 깊이 최대(소단 판정) · 처음 예산 — 띠 꼭짓점에서 L − z, 그리고 띠 둘레 «뒤 + 깊이 × n + 5 m» 상자 안 L 최대
                double dMax = 0, lMax = double.MinValue;
                foreach (var q in ring) { var lz = L(q.X, q.Y); if (lz != null) { dMax = System.Math.Max(dMax, lz.Value - q.Z); lMax = System.Math.Max(lMax, lz.Value); } }
                pw.DepthMax = dMax;
                // ★[v103.3.1 · JACK 1021 «켬/끔 + 두 칸»] 켜면 깊이와 상관없이 높이마다 — 법면이 소단 높이를 못 넘으면 단 모양이 저절로 소단을 안 만든다.
                //   ★[설치 전 검토 중간] 띠 깊이로 가르지 않는다 — 띠 깊이 3.0 m(소단 없음)와 3.1 m(위 사면이 완만하면 소단 4 ~ 5개) 사이에서 수량이 뛰었다(사면은 띠보다 훨씬 높이 올라갈 수 있다).
                // ★[v104 2판 · 검토 높음 1 · 1021 화면 없는 Civil ⑧b] 도랑(패널식)도 보통 경사로 짓고 앞면 선 앞쪽만 남긴다(ClipFront) — 옆면을 최소 구배(거의 수직)로 세우면
                //   Civil 삼각망이 못 받았다(다듬기 «뒤집기 흉내 315mm»). 뒤쪽 법면은 벽 뒤라 잘려 나가 벽 뒤 터파기 0.
                pw.Benches = st.BenchOn;
                double n0 = st.Slope;
                double reach = sl.BackMax + 5.0 + System.Math.Max(0, dMax) * n0 + (pw.Benches ? System.Math.Floor(dMax / st.BenchH) * st.BenchW : 0);
                double bx0 = ring.Min(q => q.X) - reach, bx1 = ring.Max(q => q.X) + reach, by0 = ring.Min(q => q.Y) - reach, by1 = ring.Max(q => q.Y) + reach;
                for (double xx = bx0; xx <= bx1; xx += 2.0) for (double yy = by0; yy <= by1; yy += 2.0) { var lz = L(xx, yy); if (lz != null) lMax = System.Math.Max(lMax, lz.Value); }
                pw.Rise = System.Math.Max(1.0, (lMax == double.MinValue ? pw.ZbMin + 5 : lMax) - pw.ZbMin + 1.0);
                double dep = b.WallHMax + sl.DepthMax;
                pw.Reach = sl.BackMax + dep * n0 + (pw.Benches ? System.Math.Floor(dep / st.BenchH) * st.BenchW : 0);
                works.Add(pw);
            }
            bool BuildPiece(PieceWork pw, int idx)
            {
                for (int attempt = 0; attempt < 4; attempt++)
                {
                    try
                    {
                        var p = Params(st.Slope, pw.Rise, pw.Benches, st.BenchH, st.BenchW);
                        var vs = GradingGeometry.Build(pw.Ring, sC, p, up: true, null);
                        if (!vs.HasSlope || vs.Rings.Count == 0) { pw.Note = "법면이 안 만들어졌다"; return false; }
                        var outer = vs.Rings[^1];
                        // 바깥 링이 어디서나 기준면 위인가(전체 면에서 읽는다 — 재검토 낮음)
                        bool above = true;
                        for (int i = 0; i < outer.Count && above; i++)
                        {
                            var a = outer[i]; var c = outer[(i + 1) % outer.Count];
                            foreach (var (x, y, z) in new[] { (a.X, a.Y, a.Z), ((a.X + c.X) / 2, (a.Y + c.Y) / 2, (a.Z + c.Z) / 2) })
                            { var lz = L(x, y); if (lz != null && lz.Value >= z) { above = false; break; } }
                        }
                        if (!above)
                        {
                            if (pw.Rise >= 60) { pw.Note = "예산 60m에서도 기준면을 못 만남(JACK 결정 5의 예외)"; return false; }
                            pw.Rise = System.Math.Min(60, pw.Rise * 2); pw.Grown = true; continue;
                        }
                        // ★[1021 시험 ③] 바닥 줄도 브레이크라인으로 — 바닥이 그 줄의 높이를 따라 평평하게(띠 테두리만으로는 Civil이 바닥을 비스듬히 이었다)
                        //   ★[v104 · V34 L형 계측] 양 끝 5 cm를 줄인다 — 끝점이 띠 끝 변에 닿으면 Civil이 그 자리에 꼭짓점을 넣고, 법면 쪽 점과 한 줄로 서서 납작한 세모가 생겼다
                        var lines = new System.Collections.Generic.List<System.Collections.Generic.List<Point3>>(vs.CornerLines);
                        var bl = WallExcav.Trim(pw.Bottom.Pts, 0.05);
                        if (bl.Count >= 2) lines.Add(bl);
                        var vid = GradingBuilder.BuildVirtualSlope(db, tr, vs.Rings, PieceName, lines, groundId);
                        pieceIds.Add(vid);
                        var vTin = (TinSurface)tr.GetObject(vid, OpenMode.ForRead);
                        var tris = PureWithWallsBuilder.ReadTris(vTin, out int lost);
                        try { tr.GetObject(vid, OpenMode.ForWrite).Erase(); } catch { }
                        if (lost > 0) { pw.Note = $"조각 삼각형을 읽다 {lost}개 버림"; return false; }
                        var kept = WallExcav.DropOutside(tris, outer, out int dropped);
                        if (dropped > 0) pw.Note = $"껍질 메움 삼각형 {dropped}개 버림";
                        // ★★[v105.3 · 계획_v105_3.md 6 · 1023 V47 덤프] 벽 끝 절벽 — 띠 밖 꼭짓점을 참 원뿔(띠 둘레마다 바닥 + 법면 오름의 최솟값)으로 낮춘다.
                        //   Build는 바닥 높이가 길이 따라 바뀌는 띠를 «가장 가까운 바닥 + 오름»으로 올려, 더 먼 낮은 바닥에서 오르는 법면보다 최대 4.9 m 높았다(덜 판 것 · 만나는 줄에서 절벽).
                        var kept0 = kept;
                        kept = WallExcav.ConeLower(kept, pw.Ring, SpecOf(st), out int nLow, out double dLow);
                        pw.Low = nLow; pw.LowMax = dLow;
                        // ★[계획 검토 중간 8] 낮춘 뒤 바깥 링이 아직 기준면 위인가 — 아니면 예산 두 배(위와 같은 길)
                        var zLow = new System.Collections.Generic.Dictionary<(double, double), double>();
                        foreach (var t3 in kept) { zLow[(t3.A.X, t3.A.Y)] = t3.A.Z; zLow[(t3.B.X, t3.B.Y)] = t3.B.Z; zLow[(t3.C.X, t3.C.Y)] = t3.C.Z; }
                        double Zl(Point3 q) => zLow.TryGetValue((q.X, q.Y), out double zq) ? zq : q.Z;
                        bool above2 = true;
                        for (int i = 0; i < outer.Count && above2; i++)
                        {
                            var a = outer[i]; var c = outer[(i + 1) % outer.Count];
                            double za = Zl(a), zc = Zl(c);
                            foreach (var (x, y, z) in new[] { (a.X, a.Y, za), ((a.X + c.X) / 2, (a.Y + c.Y) / 2, (za + zc) / 2) })
                            { var lz = L(x, y); if (lz != null && lz.Value >= z) { above2 = false; break; } }
                        }
                        if (!above2)
                        {
                            // ★[설치 전 검토 낮음 4] 더 못 늘리면(예산 60 · 마지막 시도) 조각을 버리지 않고 낮추기 전 면을 쓴다(v105.2와 같은 면 · 벽 끝 절벽은 남음)
                            if (pw.Rise >= 60 || attempt == 3)
                            {
                                pw.Note = (pw.Note.Length > 0 ? pw.Note + "·" : "") + "원뿔 낮추기 안 함(낮춘 바깥 링이 기준면 아래)";
                                pw.Low = 0; pw.LowMax = 0;
                                pw.Raw = kept0; pw.Outer = outer;
                                return true;
                            }
                            pw.Rise = System.Math.Min(60, pw.Rise * 2); pw.Grown = true; continue;
                        }
                        pw.Raw = kept; pw.Outer = outer;
                        return true;
                    }
                    catch (System.Exception exP) { pw.Note = $"조각 짓기 예외 {exP.GetType().Name}: {exP.Message}"; return false; }
                }
                pw.Note = "예산을 늘려도 기준면을 못 만남";
                return false;
            }
            for (int i = 0; i < works.Count; i++)
                if (!BuildPiece(works[i], i)) notes.Add($"{works[i].Bottom.Src} 조각 빠짐 — {works[i].Note}");
            // ★★[v104 · 1021 화면 없는 Civil ⑧b · V34] 도랑(패널식) 자르기 — 제 조각은 앞면 선 뒤(벽 뒤 법면)를 들어내고, 다른 조각은 그 앞면 선 뒤 2 mm 띠를 들어낸다
            //   (도랑 바닥과 벽 뒤 조각 — 위 단 도랑 등 — 이 깎기면(세로 벽)에서 한 점 두 높이로 만나지 않게 두 구역으로 가른다).
            void ApplyClips()
            {
                var trenches = works.Where(w => w.Raw != null && w.Sec.Trench).ToList();
                foreach (var w in works.Where(w => w.Raw != null))
                {
                    var t = w.Raw!;
                    int cutAll = 0; var cws = new System.Collections.Generic.List<string>();
                    foreach (var k in trenches)
                    {
                        // 앞면 선을 조각이 닿는 만큼 곧게 늘인다 — 선 끝 너머 도랑 뒤쪽도 들어내고(제 조각), 그 늘인 선 뒤에도 틈을 둔다(다른 조각)
                        double far = k.Rise * (st.Slope + (st.BenchOn ? st.BenchW / System.Math.Max(0.5, st.BenchH) : 0)) + k.Sec.FrontMax + 5.0;
                        double dist = k == w ? double.PositiveInfinity : WallExcav.TrenchGap;
                        t = WallExcav.ClipBehind(t, k.Bottom, dist, out int cutN, out string cw, extend: far);
                        cutAll += cutN;
                        if (cw.Length > 0) cws.Add(cw);
                    }
                    w.ClipNote = trenches.Count == 0 ? "" : (w.Sec.Trench ? $"도랑 — 벽 뒤 법면 들어냄(걸친 · 뒤 삼각형 {cutAll})" : cutAll > 0 ? $"도랑 앞면 선 뒤 틈 {WallExcav.TrenchGap * 1000:F0}mm(삼각형 {cutAll})" : "")
                                 + (cws.Count > 0 ? " ⚠" + string.Join(" · ", cws.Distinct()) : "");
                    w.Piece = t.Count == 0 ? null : new WallDaylight.ExcavPiece { Tris = t, Outer = w.Outer };
                    if (t.Count == 0) w.Note += (w.Note.Length > 0 ? "·" : "") + "도랑 자르기 뒤 남은 삼각형 없음";
                }
            }
            ApplyClips();
            long tPieces = sw.ElapsedMilliseconds - t1;
            var good = works.Where(w => w.Piece != null).ToList();
            R.Pieces = good.Count;
            if (good.Count == 0) return Fail("조각을 하나도 못 지었다 · " + string.Join(" · ", notes));

            // 5. 아래 껍질 — S · G는 조각 바깥 링 상자 + 1 m 안 삼각형만
            long t2 = sw.ElapsedMilliseconds;
            var sAll = PureWithWallsBuilder.ReadTris(sTin, out int lS);
            var gAll = PureWithWallsBuilder.ReadTris(gTin, out int lG);
            if (lS + lG > 0) return Fail($"정지면 · 원지반 삼각형을 읽다 {lS + lG}개를 버렸다");
            // ★[설치 전 검토 낮음] 상자는 조각이 바뀔 때마다(예산을 늘려 다시 지은 뒤) 다시 잰다 — 첫 상자로 고정하면 늘어난 자리에 기준면이 없어 «기준면 없음» 판정 3이 났다
            System.Collections.Generic.List<Tri> sTris = null!, gTris = null!;
            void PickSG()
            {
                double ex0 = good.Min(w => w.Piece!.Outer.Min(q => q.X)) - 1, ex1 = good.Max(w => w.Piece!.Outer.Max(q => q.X)) + 1;
                double ey0 = good.Min(w => w.Piece!.Outer.Min(q => q.Y)) - 1, ey1 = good.Max(w => w.Piece!.Outer.Max(q => q.Y)) + 1;
                bool InBox(Tri t) => System.Math.Max(t.A.X, System.Math.Max(t.B.X, t.C.X)) >= ex0 && System.Math.Min(t.A.X, System.Math.Min(t.B.X, t.C.X)) <= ex1
                                  && System.Math.Max(t.A.Y, System.Math.Max(t.B.Y, t.C.Y)) >= ey0 && System.Math.Min(t.A.Y, System.Math.Min(t.B.Y, t.C.Y)) <= ey1;
                sTris = sAll.Where(InBox).ToList();
                gTris = gAll.Where(InBox).ToList();
            }
            PickSG();
            // 계측 — 환경 변수 DH_EXCAV_DUMP 폴더가 있으면 엔진 입력을 글 파일로(오프라인 재현 · 출하 PC에는 없는 변수)
            try
            {
                string? dumpDir = System.Environment.GetEnvironmentVariable("DH_EXCAV_DUMP");
                if (!string.IsNullOrWhiteSpace(dumpDir) && System.IO.Directory.Exists(dumpDir))
                {
                    string stamp0 = $"{System.DateTime.Now:HHmmss_fff}";
                    WallDaylight.DumpEnvelopeInput(System.IO.Path.Combine(dumpDir, $"excav_{stamp0}.txt"), sTris, gTris, good.Select(w => w.Piece!).ToList());
                    // ★[v104 계측] 조각마다 바닥 줄 · 띠 링 · 구간(폭) — 조각 모양을 오프라인으로 본다
                    var ci0 = System.Globalization.CultureInfo.InvariantCulture;
                    using var wr0 = new System.IO.StreamWriter(System.IO.Path.Combine(dumpDir, $"rings_{stamp0}.txt"));
                    foreach (var w in good)
                    {
                        wr0.WriteLine($"PIECE {w.Bottom.Src} soil {w.Bottom.Soil} trench {w.Sec.Trench} rise {w.Rise.ToString("R", ci0)} runs {string.Join(";", w.Sec.Runs.Select(r => $"{r.S0:F3}-{r.S1:F3}:{r.Back:F3}/{r.Front:F3}"))}");
                        wr0.WriteLine($"BOTTOM {w.Bottom.Pts.Count}"); foreach (var q in w.Bottom.Pts) wr0.WriteLine(string.Format(ci0, "{0:R} {1:R} {2:R}", q.X, q.Y, q.Z));
                        wr0.WriteLine($"RING {w.Ring.Count}"); foreach (var q in w.Ring) wr0.WriteLine(string.Format(ci0, "{0:R} {1:R} {2:R}", q.X, q.Y, q.Z));
                    }
                }
            }
            catch { }
            WallDaylight.EnvelopeResult env = WallDaylight.LowerEnvelope(sTris, gTris, good.Select(w => w.Piece!).ToList());
            // 예산 끝에 닿은 조각은 그 조각만 예산을 두 배로 다시 짓는다(두 번까지)
            // ★[v104 설치 전 검토 낮음 3] 늘릴 조각이 있을 때만 — 도랑 조각은 늘 «예산 끝»이라 종전엔 패널식이 하나만 있어도 껍질(가장 비싼 단계)을 두 번 더 돌았다
            bool AnyGrow() => Enumerable.Range(0, System.Math.Min(env.PieceRim.Length, good.Count)).Any(i => env.PieceRim[i] && good[i].Rise < 60 && !good[i].Sec.Trench);
            for (int round = 0; round < 2 && AnyGrow(); round++)
            {
                for (int i = 0; i < good.Count; i++)
                    if (env.PieceRim[i] && good[i].Rise < 60 && !good[i].Sec.Trench)   // ★[v104 2판 · 검토 높음 1] 도랑은 예산 늘리기에서 뺀다
                    {
                        good[i].Rise = System.Math.Min(60, good[i].Rise * 2); good[i].Grown = true;
                        if (!BuildPiece(good[i], i)) notes.Add($"{good[i].Bottom.Src} 조각을 예산을 늘려 다시 못 지었다(앞 조각을 그대로 씀) — {good[i].Note}");   // ★[검토 낮음 6]
                    }
                ApplyClips();
                good = good.Where(w => w.Piece != null).ToList();
                PickSG();
                env = WallDaylight.LowerEnvelope(sTris, gTris, good.Select(w => w.Piece!).ToList());
            }
            // ★[v103.3.1 · JACK 1021 «체크하고 종단 그렸는데도 안나와»] 개발 PC에서 못 하면 엔진 입력을 진단이력\excav에 남긴다(최근 6개 · 오프라인 재현용 · 마지막 계산의 입력)
            if (env.Tier == 3) { string dmp = DumpOnFail(sTris, gTris, good.Select(w => w.Piece!).ToList()); if (dmp.Length > 0) failLog = dmp; }
            long tEnv = sw.ElapsedMilliseconds - t2;
            for (int i = 0; i < good.Count; i++)
            {
                if (good[i].Sec.Trench) { }   // 도랑은 앞면 선(깎기면)에서 끝난다 — 잘린 가장자리가 «예산 끝»으로 잡히는 것이 맞다
                else if (env.PieceRim.Length > i && env.PieceRim[i]) notes.Add($"{good[i].Bottom.Src} — 예산 끝까지 팠다(기준면을 못 만남 · 결정 5의 예외)");
                else if (good[i].Grown) notes.Add($"{good[i].Bottom.Src} — 위 사면이 터파기 경사보다 가파를 수 있음(벽 뒤로 보통 거리 {good[i].Reach:F1}m보다 멀리 팠다)");
            }
            // ★[v103.3.1] 못 했을 때도 로그에 앞면 · 조각을 남긴다(1021 현장 로그엔 껍질 요약만 있어 어느 옹벽 · 어느 조각인지 못 갈랐다)
            failLog = $"{facesTxt} · {secTxt} · 조각 {good.Count}/{works.Count}({string.Join(" ", good.Select(w => $"{w.Bottom.Src}:깊이{w.DepthMax:F1}m{(w.Benches ? "·소단" : "")}·예산{w.Rise:F0}·삼각형{w.Piece!.Tris.Count}{(w.Note.Length > 0 ? "·" + w.Note : "")}{(w.ClipNote.Length > 0 ? "·" + w.ClipNote : "")}"))})"
                    + $" · 정지면 {sTris.Count} · 원지반 {gTris.Count} · 시간 앞면 {tFaces} · 조각 {tPieces} · 껍질 {tEnv}ms" + (failLog.Length > 0 ? " · " + failLog : "");
            if (env.Tier == 3 || env.Tris == null) return Fail("터파기면 계산 못 함 — " + env.Fail + $" · {env.Summary}");
            if (env.Tier == 2) notes.Add("터파기면 «거의»(판정 2) — " + env.Fail);

            // 6. LandXML → 임시 면 → 되읽기 → 굳힘
            long t3 = sw.ElapsedMilliseconds;
            var final = CivilSafeMesh.FixOutside(env.Tris, _ => true, out var ofx);
            try { string fz0 = GradingBuilder.FreezeAllXmlBacked(tr); if (fz0.Length > 0) log.AppendLine("  옹벽 터파기 — 굳힘: " + fz0); } catch { }
            try { string td = GradingBuilder.TidyXml(db, tr); if (td.Length > 0) log.AppendLine("  옹벽 터파기 — " + td); } catch { }
            string xml = GradingBuilder.NewXmlPath(db, "옹벽터파기");
            WallDaylight.WriteLandXmlTin(xml, "WALLEXCAV", final, 1e-7, out int np, out int nf, out int nd);
            ObjectId cid = TinSurface.CreateFromLandXML(db, TempName, xml, "WALLEXCAV");
            if (cid.IsNull) throw new System.InvalidOperationException("CreateFromLandXML이 빈 ObjectId를 돌려줬다");
            made = cid;
            var cTin = (TinSurface)tr.GetObject(cid, OpenMode.ForWrite);
            int civPts = -1; try { civPts = cTin.GetGeneralProperties().NumberOfPoints; } catch { }
            var theirs = PureWithWallsBuilder.ReadTris(cTin, out int l4);
            var cmp = CivilSafeMesh.Compare(final, theirs);
            bool gate = l4 == 0 && nd == 0 && theirs.Count == nf && civPts == np && cmp.Exceptions == 0 && cmp.SymArea <= 1e-8 && cmp.Hausdorff <= 1e-6 && cmp.MaxDz <= 1e-3;
            string back = $"Civil 점 {civPts}/{np} · 삼각형 {theirs.Count}/{nf} · {cmp.Summary}" + (nd > 0 ? $" · ⚠넓이 0이라 뺀 면 {nd}" : "");
            if (!gate)
            {
                // ★[v103.3.2 · 1021 17:06 현장 계측] Civil이 버리거나 바꾼 삼각형을 적는다(자리 · 넓이 · 높이(가장 긴 변에서) · 가장 짧은 변) — 까닭을 가리려고
                failLog = MissingTris(final, theirs) + " · 아래 껍질 " + env.Summary + (failLog.Length > 0 ? " · " + failLog : "");
                try { cTin.Erase(); } catch { }
                made = ObjectId.Null;
                return Fail("Civil이 넘긴 터파기면을 그대로 안 받았다 — " + back);
            }
            string frz = GradingBuilder.FreezeXmlBacked(cTin);
            if (frz.Length > 0) notes.Add($"터파기면을 굳히지 못했다({frz}) — 원본 XML이 바뀌면 면이 바뀔 수 있다");
            // 낡은 터파기면 — 선형이 안 쓰는 것만 지운다
            var refd = new System.Collections.Generic.HashSet<string>();
            foreach (ObjectId aid in civ.GetAlignmentIds())
                try { var r0 = ReadAlign(tr, tr.GetObject(aid, OpenMode.ForRead)); if (r0 != null && r0.Value.Handle.Length > 0) refd.Add(r0.Value.Handle); } catch { }
            int erased = 0, keptOld = 0;
            foreach (var e in ex)
            {
                if (e.Id.IsErased) continue;
                if (refd.Contains(e.Id.Handle.ToString())) { keptOld++; continue; }
                try { tr.GetObject(e.Id, OpenMode.ForWrite).Erase(); erased++; } catch { }
            }
            string nm = GradingBuilder.UniqueName(db, tr, Name);
            cTin.Name = nm;
            try { cTin.Description = $"DH 옹벽 뒷채움 터파기면(종단 · 횡단 · 수량용) — {System.DateTime.Now:yyyy-MM-dd HH:mm} · {GradingSettings.Version} · {st.Text}"; } catch { }
            string warn = string.Join(" · ", notes);
            WriteStamp(tr, cTin, R.Stamp, $"{System.DateTime.Now:yyyy-MM-dd HH:mm:ss} · {GradingSettings.Version}", warn);
            // ★★[v104 · 계획 6] 지은 조각의 단면 줄 · 쓴 표 스냅숏 — 횡단 수량이 구조물을 뺄 때 표를 다시 찾지 않고 이것을 쓴다
            var rec = WallExcav.RecordOf(good.Select(w => w.Sec), st.Margin, st.Common(0.01), k => k == st.CutKind ? tCut : tFill);
            WriteSecRecord(tr, cTin, rec);
            GradingBuilder.SetSurfaceVisible(tr, Name, false);
            made = ObjectId.Null;
            R.State = State.Built; R.SurfaceId = cid; R.SurfaceName = nm; R.Why = warn;
            WallBackBuilder.Ensure(db, tr, groundId, R, st, sC, gC, log);   // ★[v104 B] 같은 트랜잭션 — 터파기면 기록 · 삼각형을 바로 읽는다
            tr.Commit();
            long tImp = sw.ElapsedMilliseconds - t3;
            log.AppendLine($"  옹벽 터파기 — 지었다 {nm} · {st.Text} · {facesTxt} · {secTxt} · 단면 기록 {rec.Lines.Count}줄 {rec.Lines.Sum(l => l.Pts.Count)}점 · 조각 {good.Count}/{works.Count}"
                         + $"({string.Join(" ", good.Select(w => $"{w.Bottom.Src}:깊이{w.DepthMax:F1}m{(w.Benches ? "·소단" : "")}·예산{w.Rise:F0}{(w.Low > 0 ? $"·원뿔 낮춤 {w.Low}점(최대 {w.LowMax:F2}m)" : "")}{(w.Note.Length > 0 ? "·" + w.Note : "")}{(w.ClipNote.Length > 0 ? "·" + w.ClipNote : "")}"))})"
                         + $" · 아래 껍질 {env.Summary}" + (ofx.Flipped > 0 ? $" · Civil이 고칠 면 먼저 뒤집음 {ofx.Flipped}" : "")
                         + $" · 되읽기 {back} · {(frz.Length == 0 ? "굳힘" : "⚠굳힘 실패 " + frz)} · 옛 면 지움 {erased}(선형이 쓰는 것 {keptOld} 남김)"
                         + $" · {sw.ElapsedMilliseconds}ms(앞면 {tFaces} · 조각 {tPieces} · 껍질 {tEnv} · 가져오기 {tImp}) · 도장 {R.Stamp}"
                         + (warn.Length > 0 ? " · ⚠" + warn : ""));
            return R;

            static string MissingTris(System.Collections.Generic.IReadOnlyList<Tri> ours, System.Collections.Generic.IReadOnlyList<Tri> theirs)
            {
                static (long, long) K(Point3 q) => ((long)System.Math.Round(q.X / 1e-7), (long)System.Math.Round(q.Y / 1e-7));
                static ((long, long), (long, long), (long, long)) Key(Tri t) { var a = new[] { K(t.A), K(t.B), K(t.C) }; System.Array.Sort(a); return (a[0], a[1], a[2]); }
                var tk = new System.Collections.Generic.HashSet<((long, long), (long, long), (long, long))>(theirs.Select(Key));
                var ok = new System.Collections.Generic.HashSet<((long, long), (long, long), (long, long))>(ours.Select(Key));
                static string D(Tri t)
                {
                    double e1 = System.Math.Sqrt((t.A.X - t.B.X) * (t.A.X - t.B.X) + (t.A.Y - t.B.Y) * (t.A.Y - t.B.Y));
                    double e2 = System.Math.Sqrt((t.B.X - t.C.X) * (t.B.X - t.C.X) + (t.B.Y - t.C.Y) * (t.B.Y - t.C.Y));
                    double e3 = System.Math.Sqrt((t.C.X - t.A.X) * (t.C.X - t.A.X) + (t.C.Y - t.A.Y) * (t.C.Y - t.A.Y));
                    double ar = System.Math.Abs((t.B.X - t.A.X) * (t.C.Y - t.A.Y) - (t.C.X - t.A.X) * (t.B.Y - t.A.Y)) / 2;
                    double lo = System.Math.Max(e1, System.Math.Max(e2, e3)), sh = System.Math.Min(e1, System.Math.Min(e2, e3));
                    return string.Format(System.Globalization.CultureInfo.InvariantCulture, "({0:F4},{1:F4}) 넓이 {2:E2}㎡ · 높이 {3:F1}µm · 짧은 변 {4:F1}µm · z {5:F3}/{6:F3}/{7:F3}",
                        (t.A.X + t.B.X + t.C.X) / 3, (t.A.Y + t.B.Y + t.C.Y) / 3, ar, lo > 0 ? 2 * ar / lo * 1e6 : 0, sh * 1e6, t.A.Z, t.B.Z, t.C.Z);
                }
                var gone = ours.Where(t => !tk.Contains(Key(t))).ToList();
                var added = theirs.Where(t => !ok.Contains(Key(t))).ToList();
                return $"Civil이 버린 · 바꾼 삼각형 {gone.Count}: " + string.Join(" | ", gone.Take(4).Select(D))
                     + (added.Count > 0 ? $" · Civil이 새로 만든 삼각형 {added.Count}: " + string.Join(" | ", added.Take(4).Select(D)) : "");
            }

            static string DumpOnFail(System.Collections.Generic.List<Tri> s, System.Collections.Generic.List<Tri> g, System.Collections.Generic.List<WallDaylight.ExcavPiece> p)
            {
                if (!M0Dump.IsDevPc()) return "";
                try
                {
                    string root = System.IO.Path.Combine(System.IO.Path.GetDirectoryName(System.IO.Path.GetFullPath(DiagLog.FilePath))!, "진단이력", "excav");
                    System.IO.Directory.CreateDirectory(root);
                    string path = System.IO.Path.Combine(root, $"excav_{System.DateTime.Now:yyyyMMdd_HHmmss}.txt");
                    WallDaylight.DumpEnvelopeInput(path, s, g, p);
                    foreach (var old in new System.IO.DirectoryInfo(root).GetFiles("excav_*.txt").OrderByDescending(f => f.Name).Skip(6))
                        try { old.Delete(); } catch { }
                    return "(개발 PC) 입력을 남김 " + path;
                }
                catch (System.Exception exD) { return "(개발 PC) 입력 남기기 실패 " + exD.Message; }
            }

            Result Fail(string why)
            {
                R.State = State.Failed;
                if (R.Stamp.Length == 0) R.Stamp = HeadOf(st, tp, "", "", groundId.IsNull ? "" : groundId.Handle.ToString());   // ★[검토 낮음 10] 빈 도장이면 횡단이 엉뚱한 «바뀜»을 말한다
                R.Why = why + (notes.Count > 0 && !why.Contains(notes[0]) ? " · " + string.Join(" · ", notes) : "");
                log.AppendLine($"  옹벽 터파기 — {R.StatusText} · {sw.ElapsedMilliseconds}ms" + (failLog.Length > 0 ? "\n    " + failLog : ""));
                try { tr.Commit(); } catch { }
                return R;
            }
        }
        catch (System.Exception ex)
        {
            log.AppendLine($"  옹벽 터파기 — ⚠짓다 터졌다({ex.GetType().Name}: {ex.Message})");
            try
            {
                using var tr2 = db.TransactionManager.StartTransaction();
                foreach (var id in pieceIds.Append(made))
                    try { if (!id.IsNull && !id.IsErased) tr2.GetObject(id, OpenMode.ForWrite).Erase(); } catch { }
                tr2.Commit();
            }
            catch { }
            R.State = State.Failed;
            R.Why = $"터파기면을 못 지었다({ex.GetType().Name}: {ex.Message})";
            return R;
        }
    }

    /// <summary>★[v105.3] 터파기 법면 제원(Core) — 조각 <see cref="Params"/>와 같은 꼴(경사 = max(설정, 최소) · 소단 끄면 한 장).
    /// 3D 원뿔 낮추기 · 횡단 2D 터파기선이 같이 쓴다.</summary>
    internal static WallExcav.SlopeSpec SpecOf(WallExcav.Settings st)
        => new(System.Math.Max(st.Slope, GradingSettings.MinSlope), st.BenchOn, st.BenchH, st.BenchOn ? st.BenchW : 0.0);

    /// <summary>터파기 제원 — 경사 n · 소단을 두면 <paramref name="benchH"/>마다 <paramref name="benchW"/> 소단(★[v103.3.1 · JACK 1021] 도킹바 «소단» 켬 · 높이 · 폭 — 종전 고정 «5 m 넘으면 3 m마다 1 m»)
    /// · 아니면 한 장의 법면(DHEXCAV와 같은 꼴).</summary>
    static GradingParams Params(double n, double rise, bool benches, double benchH, double benchW)
    {
        double sl = System.Math.Max(n, GradingSettings.MinSlope);
        double bh = benches ? benchH : rise + 1.0;
        double bw = benches ? benchW : 0.0;
        return new GradingParams
        {
            CutBenchHeight = bh, FillBenchHeight = bh,
            CutBenchWidth = bw, FillBenchWidth = bw,
            CutSlope = sl, FillSlope = sl,
            CellSize = GradingSettings.CellSize,
            MaxBenches = benches ? (int)System.Math.Ceiling(rise / benchH) + 2 : 4,
            MaxRise = rise,
            VertexSpacing = GradingSettings.VertexSpacing,
            MinSlope = GradingSettings.MinSlope,
            WallGateSlope = GradingSettings.WallGateSlope,
            MinFaceRun = GradingSettings.MinFaceRun,
            MiterConvex = GradingSettings.MiterConvex,
            MiterLimit = GradingSettings.MiterLimit,
        };
    }
}
