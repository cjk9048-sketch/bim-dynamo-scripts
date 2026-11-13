﻿using System.Runtime.CompilerServices;
using Autodesk.AutoCAD.ApplicationServices;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.Runtime;
using CivilDb = Autodesk.Civil.DatabaseServices;
using AcadApp = Autodesk.AutoCAD.ApplicationServices.Application;

namespace DH.Grading.Civil;

/// <summary>★[v114.2 · JACK 1105 «명령 뒤 되돌리기 기록 비움»] Civil 결함 회피 — 지표면 종단(EG · <c>Profile.CreateFromSurface</c>)이 따르는 면을
/// 바꾼(이름 · 설명 · 보이기 · 지우기 · 합성 바꿔 끼움 …) 뒤 UNDO 하면 Civil이 죽는다(1105 실측 · 화면 있는 Civil도 같음 · Autodesk 쪽).
/// 실측 회피 = 바꾼 뒤 «UNDO 조정 없음 → UNDO 모두»로 되돌리기 기록을 비운다(그 뒤 UNDO는 되돌릴 것이 없어 안 죽는다).
/// <para>① 알아채기: 우리 명령(«DH»로 시작) 도중 바뀌거나 지워진 Civil 지표면 · 지워진 EG 종단을 모은다(도면 이벤트 — 명령 도는 동안만 단다).
/// ② 비우기: 모은 면을 따르는 EG 종단이 있을 때만 — 종단의 원본 면(<c>DataSourceId</c>)과 그 면이 붙여넣은 면(여러 단)까지 본다 · 못 읽으면 위험으로 침.
/// 없으면 사용자의 되돌리기 기록은 그대로.
/// ③ 언제: <see cref="FlushNow"/>를 부르는 명령(합성 · 보기)은 명령 끝에서 바로(명령 안 <c>Editor.Command</c> — 화면 없는 시험도 같은 길) ·
/// 나머지는 명령이 끝난 뒤 Idle 한 번에서(이 도면이 앞 도면이고 도는 명령이 없을 때만) <c>DHUNDOCLR</c>를 건다 — 다른 명령 프롬프트에 끼어들지 않게.
/// ④ 저장 뒤 한 번 더: 바꾼 뒤 <b>첫 .dwg 저장</b>에서 Civil이 미뤄 둔 종단 · 면 갱신을 하고 그것이 되돌리기 기록에 남는다 — 그 뒤 UNDO도 죽는다
/// (1105 실측: 합성 → 비움 → 저장 → UNDO 죽음 · 합성 → 저장 → 비움 → UNDO 안 죽음 · 두 번째 저장부터는 안 죽음 · 재생성 · 종단 읽기 · UpdateExt로는 미리 못 일으킴).
/// 그래서 첫 저장 뒤 한 번 더(조용히) 비운다. 자동 저장(.sv$) · 내보내기 · 닫기 중 저장은 안 친다.
/// 화면 없는 Civil은 Idle도 걸어 둔 문자열도 스크립트 도중에 안 돈다(1105 실측) → 그때는 한 줄만 남긴다(시험 스크립트가 DHUNDOCLR를 직접 친다).</para></summary>
public sealed class UndoGuard
{
    sealed class State
    {
        /// <summary>지금 도는 우리 명령(없으면 null) — 안에서 부른 UNDO · ZOOM 같은 남의 명령은 안 친다.</summary>
        public string? Cmd;
        /// <summary>마지막으로 비운 뒤 우리 명령이 바꾼 지표면.</summary>
        public readonly HashSet<ObjectId> Touched = new();
        /// <summary>우리 명령이 지운 EG 종단(되돌리면 함께 살아난다 — 그 원본 면도 본다).</summary>
        public readonly HashSet<ObjectId> ErasedProfiles = new();
        /// <summary>비웠지만 아직 .dwg 저장을 안 거친 면 — 첫 저장 뒤 한 번 더 비운다.</summary>
        public readonly HashSet<ObjectId> Armed = new();
        public readonly HashSet<ObjectId> ArmedProfiles = new();
        /// <summary>저장이 끝나 한 번 더 비워야 한다(조용히).</summary>
        public bool AfterSave;
        /// <summary>DHUNDOCLR를 걸어야 한다(Idle이 건다).</summary>
        public bool Pending;
        public bool IdleHooked;
        /// <summary>이 명령 안에서 이미 비우고 알렸다 — 명령 끝에서 또 걸면 조용히.</summary>
        public bool ClearedInCmd;
        public bool QuietNext;
        public bool DbHooked;
        public ObjectEventHandler? OnMod;
        public ObjectErasedEventHandler? OnErase;
    }

    enum Cleared { Done, Nothing, Failed }

    static readonly ConditionalWeakTable<Document, State> _st = new();

    const string Msg = "\n[되돌리기] 종단이 따르는 지표면이 바뀌어 Ctrl+Z 기록을 비웠습니다(Civil 결함 회피) — 되돌리기는 [사면 변환] 등 단추로";

    /// <summary>도면마다 한 번 — 이벤트를 단다(두 번 불러도 한 번). 면 바뀜 이벤트는 우리 명령이 도는 동안만 붙인다.</summary>
    internal static void Hook(Document? doc)
    {
        if (doc == null || _st.TryGetValue(doc, out _)) return;
        var st = new State();
        _st.Add(doc, st);
        st.OnMod = (_, e) => Touch(st, e.DBObject, false);
        st.OnErase = (_, e) => Touch(st, e.DBObject, e.Erased);
        try
        {
            doc.Database.SaveComplete += (_, e) => Saved(doc, st, e.FileName);
            doc.CommandWillStart += (_, e) =>
            {
                string c = (e.GlobalCommandName ?? "").ToUpperInvariant();
                if (!c.StartsWith("DH", StringComparison.Ordinal)) return;
                // 앞 명령의 끝을 못 받았으면(도면을 바꾸는 세션 명령 등) 낡은 것으로 보고 갈아 끼운다 — 보호가 꺼진 채 남지 않게
                st.Cmd = c; st.ClearedInCmd = false;
                AttachDb(doc, st, true);
            };
            doc.CommandEnded += (_, e) => End(doc, st, e.GlobalCommandName);
            doc.CommandCancelled += (_, e) => End(doc, st, e.GlobalCommandName);
            doc.CommandFailed += (_, e) => End(doc, st, e.GlobalCommandName);
        }
        catch { }
    }

    static void AttachDb(Document doc, State st, bool on)
    {
        if (st.DbHooked == on) return;
        try
        {
            var db = doc.Database;
            if (on) { db.ObjectModified += st.OnMod; db.ObjectErased += st.OnErase; }
            else { db.ObjectModified -= st.OnMod; db.ObjectErased -= st.OnErase; }
            st.DbHooked = on;
        }
        catch { }
    }

    static void Touch(State st, DBObject? o, bool erased)
    {
        if (st.Cmd == null || o == null) return;
        try
        {
            if (o is CivilDb.Surface) st.Touched.Add(o.ObjectId);
            else if (erased && o is CivilDb.Profile pf)
            {
                bool eg; try { eg = pf.ProfileType == CivilDb.ProfileType.EG; } catch { eg = true; }
                if (eg) st.ErasedProfiles.Add(o.ObjectId);
            }
        }
        catch { }
    }

    /// <summary>우리 명령이 끝남(끝 · 취소 · 실패 모두 — 커밋된 바뀜은 기록에 남는다) — 아직 안 비웠고 위험하면(또는 명령 안 저장 뒤면) DHUNDOCLR를 건다.</summary>
    static void End(Document doc, State st, string? name)
    {
        if (st.Cmd == null || !string.Equals(st.Cmd, name, StringComparison.OrdinalIgnoreCase)) return;
        st.Cmd = null;
        AttachDb(doc, st, false);
        if (st.Touched.Count == 0 && !st.AfterSave) { st.ErasedProfiles.Clear(); return; }
        bool risky;
        try { risky = Risky(doc.Database, st.Touched, st.ErasedProfiles); } catch { risky = true; }   // 못 재면 위험으로
        if (!risky) { st.Touched.Clear(); st.ErasedProfiles.Clear(); }
        if (!risky && !st.AfterSave) return;
        st.QuietNext = st.ClearedInCmd;
        Arm(doc, st, "명령 끝");
    }

    /// <summary>저장이 끝남 — 비운 뒤 첫 .dwg 저장이면 한 번 더 비우게 건다(저장이 남긴 미룬 갱신 기록). 자동 저장 · 내보내기 · 닫기 중 저장은 안 친다.</summary>
    static void Saved(Document doc, State st, string? fileName)
    {
        if (st.Armed.Count == 0) return;
        if (!(fileName ?? "").EndsWith(".dwg", StringComparison.OrdinalIgnoreCase)) return;
        try
        {
            string cip = (doc.CommandInProgress ?? "").ToUpperInvariant();
            if (cip.Contains("CLOSE") || cip.Contains("QUIT") || cip.Contains("EXIT")) return;
        }
        catch { }
        bool risky;
        try { risky = Risky(doc.Database, st.Armed, st.ArmedProfiles); } catch { risky = true; }
        if (!risky) { st.Armed.Clear(); st.ArmedProfiles.Clear(); return; }
        st.AfterSave = true;
        if (st.Cmd == null) Arm(doc, st, "저장 뒤");   // 우리 명령 안의 저장(사본 저장 등)은 그 명령 끝(End)이 건다
    }

    /// <summary>DHUNDOCLR 걸기 — Idle 한 번에서, 이 도면이 앞 도면이고 도는 명령이 없을 때만(다른 명령 프롬프트에 끼어들지 않게) · 건 뒤 Idle을 뗀다.</summary>
    static void Arm(Document doc, State st, string when)
    {
        st.Pending = true;
        if (SelfCheckLog.Headless) { try { doc.Editor.WriteMessage($"\n[되돌리기] 화면 없음 — {when} DHUNDOCLR 못 걸음"); } catch { } return; }
        if (st.IdleHooked) return;
        st.IdleHooked = true;
        EventHandler? h = null;
        h = (_, _) =>
        {
            try
            {
                bool open = false;
                foreach (Document d in AcadApp.DocumentManager) if (d == doc) { open = true; break; }
                if (open && st.Pending)
                {
                    if (AcadApp.DocumentManager.MdiActiveDocument != doc || !string.IsNullOrEmpty(doc.CommandInProgress)) return;   // 다음 Idle까지 기다림
                    doc.SendStringToExecute("DHUNDOCLR ", false, false, false);
                }
            }
            catch { }
            AcadApp.Idle -= h; st.IdleHooked = false;
        };
        AcadApp.Idle += h;
    }

    /// <summary>명령 끝에서(트랜잭션 밖 · 명령 안) 바로 비운다 — 바뀐 면을 따르는 EG 종단이 있을 때만. 못 하면(명령 밖 등) 명령 끝 길에 맡긴다.</summary>
    internal static void FlushNow(Document? doc = null) => FlushNow(doc, false);

    static void FlushNow(Document? doc, bool quiet)
    {
        doc ??= AcadApp.DocumentManager.MdiActiveDocument;
        if (doc == null || !_st.TryGetValue(doc, out var st)) return;
        if (st.AfterSave)
        {
            // 저장 뒤 한 번 더 — 조용히(처음 비울 때 이미 알렸다)
            if (Clear(doc) == Cleared.Failed) return;
            st.AfterSave = false; st.Armed.Clear(); st.ArmedProfiles.Clear();
        }
        if (st.Touched.Count == 0) { st.ErasedProfiles.Clear(); return; }
        bool risky;
        try { risky = Risky(doc.Database, st.Touched, st.ErasedProfiles); } catch { risky = true; }
        if (!risky) { st.Touched.Clear(); st.ErasedProfiles.Clear(); return; }
        var r = Clear(doc);
        if (r == Cleared.Failed) return;
        st.Armed.UnionWith(st.Touched); st.ArmedProfiles.UnionWith(st.ErasedProfiles);
        st.Touched.Clear(); st.ErasedProfiles.Clear();
        if (r == Cleared.Done && !quiet && !st.ClearedInCmd) try { doc.Editor.WriteMessage(Msg); } catch { }
        st.ClearedInCmd = true;
    }

    /// <summary>바뀐 면을 따르는 EG 종단이 있나 — 원본 면 · 그 면이 붙여넣은 면(여러 단)까지 · 지운 EG 종단도 · 원본을 못 읽으면 위험(예외는 부른 쪽이 위험으로).</summary>
    static bool Risky(Database db, HashSet<ObjectId> touched, HashSet<ObjectId> erasedProfiles)
    {
        if (touched.Count == 0) return false;
        var cd = Autodesk.Civil.ApplicationServices.CivilDocument.GetCivilDocument(db);
        using var tr = db.TransactionManager.StartOpenCloseTransaction();
        var seen = new HashSet<ObjectId>();
        bool Follows(ObjectId sid)
        {
            if (sid.IsNull || touched.Contains(sid)) return true;
            if (!seen.Add(sid)) return false;
            if (tr.GetObject(sid, OpenMode.ForRead, true) is not CivilDb.TinSurface ts) return false;
            var ops = ts.Operations;
            for (int i = 0; i < ops.Count; i++)
                if (ops[i] is CivilDb.SurfaceOperationPasteSurface ps && Follows(ps.SurfaceId)) return true;
            return false;
        }
        bool Prof(ObjectId pid, bool erased)
        {
            if (tr.GetObject(pid, OpenMode.ForRead, erased) is not CivilDb.Profile pf) return false;
            if (pf.ProfileType != CivilDb.ProfileType.EG) return false;
            return Follows(pf.DataSourceId);
        }
        foreach (ObjectId aid in cd.GetAlignmentIds())
        {
            if (aid.IsErased || tr.GetObject(aid, OpenMode.ForRead) is not CivilDb.Alignment al) continue;
            foreach (ObjectId pid in al.GetProfileIds())
                if (!pid.IsErased && Prof(pid, false)) return true;
        }
        foreach (ObjectId pid in erasedProfiles)
        {
            bool hit; try { hit = Prof(pid, true); } catch { hit = true; }
            if (hit) return true;
        }
        tr.Commit();
        return false;
    }

    /// <summary>UNDO 조정 없음 → 모두(사용자가 «하나»였으면 하나로) · 명령줄 메아리 끔 · 자동(Auto) 켬/끔은 앞과 같게.
    /// 되돌리기가 꺼져 있으면 비울 것이 없다(Nothing). 다시 켜기가 실패하면 한 번 더 · 그래도 안 되면 알린다.</summary>
    static Cleared Clear(Document doc)
    {
        var ed = doc.Editor;
        int ctl = Convert.ToInt32(AcadApp.GetSystemVariable("UNDOCTL"));
        if ((ctl & 1) == 0) return Cleared.Nothing;
        object echo = AcadApp.GetSystemVariable("CMDECHO");
        try
        {
            AcadApp.SetSystemVariable("CMDECHO", 0);
            try { ed.Command("_.UNDO", "_C", "_N"); } catch { return Cleared.Failed; }
            string back = (ctl & 2) != 0 ? "_O" : "_A";
            bool on = false;
            for (int k = 0; k < 2 && !on; k++) { try { ed.Command("_.UNDO", back); on = true; } catch { } }
            if (!on) { try { ed.WriteMessage("\n[되돌리기] ⚠되돌리기를 다시 못 켰습니다 — UNDO → 조정(C) → 모두(A)를 치세요"); } catch { } return Cleared.Failed; }
            int now = Convert.ToInt32(AcadApp.GetSystemVariable("UNDOCTL"));
            if ((now & 4) != (ctl & 4)) try { ed.Command("_.UNDO", "_Auto", (ctl & 4) != 0 ? "_ON" : "_OFF"); now = Convert.ToInt32(AcadApp.GetSystemVariable("UNDOCTL")); } catch { }
            // 8 = 명령 안 그룹 표지(이 명령 자신의 것 — 비우면 함께 없어진다 · 1105 실측 61→53) — 그 밖의 비트는 같아야 한다
            if (SelfCheckLog.Headless) ed.WriteMessage($"\n[되돌리기] UNDOCTL {ctl}→{now} · 그룹 표지 뺀 값 {ctl & ~8}→{now & ~8}");
            return Cleared.Done;
        }
        finally { try { AcadApp.SetSystemVariable("CMDECHO", echo); } catch { } }
    }

    /// <summary>명령 끝 · 저장 뒤 길이 거는 명령 — 남은 것이 위험하면 비운다(손으로 쳐도 같음 · 남은 것이 없으면 아무 말 없음).
    /// 되돌리기 표지를 안 남긴다(NoUndoMarker — 할 일이 없을 때 Ctrl+Z 한 번을 빈 걸음으로 먹지 않게).</summary>
    [CommandMethod("DHUNDOCLR", CommandFlags.NoUndoMarker)]
    public void ClearCmd()
    {
        var doc = AcadApp.DocumentManager.MdiActiveDocument;
        if (doc == null || !_st.TryGetValue(doc, out var st)) return;
        st.Pending = false;
        bool quiet = st.QuietNext; st.QuietNext = false;
        FlushNow(doc, quiet);
    }
}
