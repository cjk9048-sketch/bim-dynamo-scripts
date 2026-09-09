using System.Collections.Generic;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.EditorInput;
using Autodesk.AutoCAD.Runtime;
using AcadApp = Autodesk.AutoCAD.ApplicationServices.Application;
using AcDoc = Autodesk.AutoCAD.ApplicationServices.Document;

namespace DH.Grading.Civil;

/// <summary>★★★[계획 3단계 · JACK 0908 <i>"창 안에서 선택버튼을 누르고 폴리곤을 선택하면
/// 빨간색으로 바뀌고, 이어서 지표면 선택하고 엔터 또는 도킹창의 생성 버튼을 누르면"</i>]
/// <b>도킹창이 도면을 찍게 해 주는 살림꾼.</b>
///
/// <para><b>무엇이 어려운가.</b> <c>Editor.GetEntity</c>는 <b>명령 문맥 안</b>에서만 된다.
/// 도킹창 단추 클릭은 명령 문맥이 아니라 거기서 바로 부르면 안 되거나 AutoCAD가 죽는다.</para>
///
/// <para><b>이미 도는 길을 쓴다.</b> 지층 도킹창이 벌써 그렇게 한다 —
/// <c>SendStringToExecute</c> → <c>[CommandMethod(…, CommandFlags.Modal)]</c>.
/// 새 방식(<c>ExecuteInCommandContextAsync</c>)을 만들지 않은 이유 셋:
/// ①<b>이름 있는 명령</b>이라 리본 활성/비활성이 쓰는 <c>CommandEnded</c>와 자연스럽게 맞물린다
/// ②예외가 명령 안에서 잡혀 <b>프로세스를 안 죽인다</b> ③<c>^C^C</c>·스크립트와 같은 규약을 탄다.</para>
///
/// <para><b>★<c>ObjectId</c>가 아니라 <c>Handle</c>을 들고 있는다.</b>
/// 찍는 것과 만드는 것 사이에 시간이 흐르고, 그 사이 사용자가 되돌리기로 지웠다 살릴 수 있다.</para>
///
/// <para><b>★자물쇠는 하나다.</b> 창마다 두면 정지 창에서 찍는 중에 터파기 창 단추를 눌러
/// 그쪽이 <c>^C^C</c>를 보내 진행 중인 찍기를 죽인다. 두 창이 <b>이것 하나</b>를 나눠 쓴다.</para>
///
/// <para>★<b>기록은 <see cref="AcDoc.UserData"/>에 담는다</b> — 사전 키로 <c>Document</c>를 쓰면
/// 그 형은 <c>Equals</c>/<c>GetHashCode</c>를 재정의해 <b>네이티브 포인터</b>를 볼 개연성이 크다.
/// 닫힘 이벤트를 한 번이라도 놓치면 죽은 포인터 키가 남고, 포인터가 재사용되면
/// <b>새 도면이 옛 도면의 선택을 물려받는다</b> — 핸들 값이 작고 순차적이라
/// 엉뚱한 폴리선이 계획 경계로 잡힐 수 있다(검토 0909). 도면에 매달아 두면 같이 죽는다.</para></summary>
internal static class PickSession
{
    // ── 자리 이름 ──────────────────────────────────────────────────────────
    internal const string KeyPlan = "계획경계";
    internal const string KeyGround = "원지반";
    internal const string KeyExcav = "터파기선";

    /// <summary>도면에 매달아 두는 열쇠 — 다른 애드인과 안 부딪히게 길게 짓는다.</summary>
    private const string UserDataKey = "DH.Grading.PickSession";

    /// <summary>하나의 "고른 것".</summary>
    internal sealed class Slot
    {
        /// <summary>★<c>ObjectId</c>가 아니라 <b>핸들</b>이다(위 설명).</summary>
        internal string Handle = "";
        /// <summary>사람이 읽을 이름 — 창에 그대로 보여 준다.</summary>
        internal string What = "";
        /// <summary>빨간 표시 — <b>이 살림꾼이 들고 있는다</b>(명령이 끝나도 안 사라진다).
        /// 지표면처럼 모양을 못 읽는 것은 <c>null</c>이다.</summary>
        internal PickMark Mark;
    }

    /// <summary>★찍는 중인가 — <b>두 창이 나눠 쓰는 자물쇠 하나</b>.</summary>
    internal static bool Busy { get; private set; }

    /// <summary>고른 것이 바뀌었다 — 창이 이것을 듣고 다시 그린다.
    /// <para>★<b>지금은 구독자가 없다</b>(검토 0909 확인). 5b에서 창이, 8단계에서 리본이 붙는다.</para>
    /// <para>★<b>정적 이벤트다</b> — 창이 닫힐 때 <b>반드시 해지</b>해야 한다(안 하면 죽은 UI로 호출이 간다).</para></summary>
    internal static event System.Action Changed;

    // ── 세는 것들 ★[JACK 규칙] 자주 고치는 자리는 추적표를 둔다 ──────────────
    private static int _nPick, _nReplace, _nStale, _nDocClear, _nReject, _nUnstick;

    /// <summary>지금 어떤 명령이 도는 중인가 — 배치 훅이 남의 일 한복판에 끼어들지 않게(검토 0909).</summary>
    private static bool _inCommand;

    /// <summary>한 줄 자가검증 — <c>DHPICKSTATUS</c>가 찍는다.</summary>
    internal static string Tally =>
        $"찍는중={Busy} · 찍기 {_nPick} · 교체 {_nReplace} · 지워진것 {_nStale} · 도면정리 {_nDocClear}"
      + $" · 겹쳐누름막음 {_nReject} · 자물쇠푼횟수 {_nUnstick}"
      + $" · 표시못걷음 {PickMark.LeakedCount} · 다른도면이라보류 {PickMark.StrandedCount}"
      + $" · 붙잡고있음 {PickMark.HeldCount}";

    // ── 기록 자리 ─────────────────────────────────────────────────────────
    private static Dictionary<string, Slot> Bag(AcDoc doc, bool make)
    {
        if (doc == null) return null;
        try
        {
            if (doc.UserData.Contains(UserDataKey) && doc.UserData[UserDataKey] is Dictionary<string, Slot> m)
                return m;
            if (!make) return null;
            var n = new Dictionary<string, Slot>();
            doc.UserData[UserDataKey] = n;
            return n;
        }
        catch { return null; }
    }

    // ── 읽기 ──────────────────────────────────────────────────────────────
    /// <summary>이 도면에서 <paramref name="key"/> 자리에 무엇을 골랐나. 안 골랐으면 <c>null</c>.</summary>
    internal static Slot Peek(AcDoc doc, string key)
    {
        var m = Bag(doc, false);
        return m != null && m.TryGetValue(key, out var s) ? s : null;
    }

    /// <summary>고른 것을 지금 도면에서 찾아본다 — <b>아무것도 안 지운다</b>.
    /// <para>★[검토 0909 · 보통] 상태를 <b>보여 주는</b> 자리(창 새로고침·<c>DHPICKSTATUS</c>)는 이것을 쓴다.
    /// 종전엔 보기만 해도 지워진 것을 <b>영구히 잊어</b>, 되돌리기로 살려도 안 돌아왔다 —
    /// 핸들을 쓰기로 한 <b>이유 자체와 모순</b>이었다.</para></summary>
    internal static ObjectId TryResolve(AcDoc doc, string key, out bool erased)
    {
        erased = false;
        var s = Peek(doc, key);
        if (s == null || string.IsNullOrEmpty(s.Handle)) return ObjectId.Null;
        var id = Commands.NoriCommand.FindByHandle(doc.Database, s.Handle);   // ★선례를 그대로 쓴다(판정 규칙 한 벌)
        if (id.IsNull) erased = true;
        return id;
    }

    /// <summary>★<b>쓰기 직전</b>에 부른다 — 정말 없으면 자리를 비운다(창이 "미선택"으로 돌아간다).</summary>
    internal static ObjectId ResolveForUse(AcDoc doc, string key)
    {
        var id = TryResolve(doc, key, out bool erased);
        if (erased) { _nStale++; Forget(doc, key); }
        return id;
    }

    // ── 쓰기 ──────────────────────────────────────────────────────────────
    /// <summary>고른 것을 담고 <b>빨갛게</b> 칠한다. 같은 자리에 다시 담으면 옛 표시를 걷는다.
    /// <para>★<b>명령 문맥에서만</b> 부른다 — 여기가 트랜잭션을 연다.
    /// 패널 이벤트에서 바로 부르면 <c>eLockViolation</c>이다(이 저장소가 이미 겪은 함정).</para></summary>
    /// <param name="paint">모양을 베껴 칠할지 — 지표면은 <b>칠할 수 없다</b>(경계를 못 읽는다).</param>
    internal static void Set(AcDoc doc, string key, ObjectId id, string what, bool paint = true)
    {
        if (doc == null || id.IsNull) return;
        var m = Bag(doc, true);
        if (m == null) return;
        if (m.TryGetValue(key, out var old))
        {
            // ★<b>다시 찍으면 옛 표시부터 걷는다</b> — 안 걷으면 빨간 선이 겹쳐 쌓인다.
            try { old.Mark?.Dispose(); } catch { }
            _nReplace++;
        }
        // ★★[검토 0909 · 보통] <b>문서를 잠그고 그린다.</b>
        //   지금은 명령 안에서만 불리지만, 5·6단계에서 <b>패널 이벤트가 이것을 부르게</b> 된다 —
        //   그때 잠금 없이 트랜잭션을 열면 <c>eLockViolation</c>이다.
        //   이 저장소는 이미 그 함정을 겪고 <c>StrataDraw.Lock()</c>을 만들어 뒀다.
        //   ★명령 안에서 겹쳐 잠가도 안전하다(횟수를 센다) — 그래서 조건 없이 건다.
        PickMark mk = null;
        if (paint)
        {
            try
            {
                using var dl = doc.LockDocument();
                mk = PickMark.Paint(doc, doc.Database, id);
            }
            catch (System.Exception lx)
            {
                try { DiagLog.Append("\n■ 찍기 표시 실패(문서 잠금) — " + lx.Message + "\n"); } catch { }
            }
        }
        m[key] = new Slot
        {
            Handle = id.Handle.ToString(),
            What = what ?? "",
            Mark = mk,
        };
        _nPick++;
        Hook();
        Raise();
    }

    /// <summary>한 자리만 잊는다(표시도 걷는다).</summary>
    internal static void Forget(AcDoc doc, string key)
    {
        var m = Bag(doc, false);
        if (m == null) return;
        if (m.TryGetValue(key, out var s)) { try { s.Mark?.Dispose(); } catch { } m.Remove(key); }
        Raise();
    }

    /// <summary>이 도면에서 고른 것을 <b>전부</b> 잊는다 — 만들기가 끝났을 때·창을 닫을 때.</summary>
    internal static void ClearAll(AcDoc doc)
    {
        var m = Bag(doc, false);
        if (m == null) return;
        foreach (var s in m.Values) { try { s.Mark?.Dispose(); } catch { } }
        m.Clear();
        Raise();
    }

    /// <summary>표시만 걷고 <b>고른 것은 남긴다</b> — 도면을 잠시 떠날 때.
    /// <para>돌아오면 <see cref="Repaint"/>가 다시 칠한다. 잠깐 다른 도면을 봤다고
    /// 골라 놓은 것이 사라지면 사람이 다시 찍어야 한다.</para></summary>
    internal static void DropMarks(AcDoc doc)
    {
        var m = Bag(doc, false);
        if (m == null) return;
        foreach (var s in m.Values) { try { s.Mark?.Dispose(); } catch { } s.Mark = null; }
        Raise();
    }

    /// <summary>도면으로 돌아왔다 — 아직 살아 있는 것만 다시 칠한다.</summary>
    internal static void Repaint(AcDoc doc)
    {
        var m = Bag(doc, false);
        if (m == null) return;
        foreach (var kv in m)
        {
            if (kv.Value.Mark != null) continue;
            if (kv.Key == KeyGround) continue;                 // 지표면은 애초에 못 칠한다
            var id = Commands.NoriCommand.FindByHandle(doc.Database, kv.Value.Handle);
            if (!id.IsNull) kv.Value.Mark = PickMark.Paint(doc, doc.Database, id);
        }
        Raise();
    }

    // ── 자물쇠 ────────────────────────────────────────────────────────────
    /// <summary>찍기를 시작한다. 이미 찍는 중이면 <b>거절한다</b>.
    /// <para>패널은 <see cref="Send"/>로 <c>^C^C</c>를 붙여 보내므로 앞의 찍기가 먼저 취소된다 —
    /// 이 거절은 <b>타이핑·스크립트</b> 같은 다른 경로 대비다.</para></summary>
    internal static bool Begin(Editor ed, string what)
    {
        if (Busy)
        {
            _nReject++;
            try { ed?.WriteMessage($"\n[찍기] 이미 다른 찍기가 도는 중입니다({what}) — Esc로 취소한 뒤 다시 하세요."); }
            catch { }
            return false;
        }
        Busy = true;
        // ★[검토 0909] <b>아직 아무도 이 신호를 안 듣는다.</b> 종전 주석은 "창이 단추를 회색으로
        //   내린다"였는데 <c>Changed</c> 구독자가 <b>0명</b>이었다 — 주석이 코드보다 앞선 자리다.
        //   5b에서 창이, 8단계에서 리본이 구독한다. 그때까지는 <b>아무 일도 안 일어난다</b>.
        Raise();
        return true;
    }

    /// <summary>찍기를 끝낸다 — <b>어떤 길로 빠져나가든</b> 불러야 한다.</summary>
    internal static void End()
    {
        if (!Busy) return;
        Busy = false;
        Raise();
    }

    /// <summary>★★[검토 0909 · 높음] <b>고착된 자물쇠를 푼다.</b>
    /// <para>찍는 도중 그 도면을 닫으면 명령이 스택 되감기 없이 죽어 <c>finally</c>가 안 돈다.
    /// 자물쇠가 앱 전역이라 <b>두 창의 모든 단추가 영구히</b> 죽고, 되살릴 길이 하나도 없었다.</para></summary>
    private static void Unstick(string why)
    {
        if (!Busy) return;
        _nUnstick++;
        Busy = false;
        Raise();
        try { DiagLog.Append($"\n■ 찍기 자물쇠를 강제로 풀었다 — {why}\n"); } catch { }
    }

    private static void Raise() { try { Changed?.Invoke(); } catch { } }

    // ── 패널이 명령을 부르는 유일한 문 ─────────────────────────────────────
    /// <summary>★★[검토 0909 · 높음] <b><c>^C^C</c>를 붙여 보낸다.</b>
    ///
    /// <para>리본이 <b>일부러</b> 그렇게 한다(<see cref="RibbonApp"/>). 이 저장소가 이유를 적어 뒀다 —
    /// <i>"새 명령 문자열은 그 물음의 답으로 먹히거나 줄에 쌓여 사용자가 ESC를 누를 때까지 시작되지 않는다."</i></para>
    ///
    /// <para>안 붙이면, <c>DHPICKPLAN</c>이 선택을 기다리는 중에 패널의 [원지반 선택]을 눌렀을 때
    /// 그 문자열이 <b>선택 프롬프트의 답으로 먹힌다</b> — 아무 일도 안 일어나고 단추가 고장 난 것처럼 보인다.
    /// 그러면 <see cref="Begin"/>의 거절 메시지까지 <b>오지도 못한다</b>.</para>
    ///
    /// <para>→ 패널은 <b>반드시 이 문</b>으로 부른다. 규약을 코드가 들고 있어야 창마다 어긋나지 않는다.</para></summary>
    internal static void Send(AcDoc doc, string command)
    {
        if (doc == null || string.IsNullOrEmpty(command)) return;
        // ★ESC ESC — 리본이 쓰는 것과 <b>같은 접두</b>다(RibbonApp.CancelPrefix).
        //   ★소스에 진짜 제어문자를 박지 않는다 — 눈에 안 보여 편집기·붙여넣기가 뭉갠다.
        try { doc.SendStringToExecute("\u0003\u0003" + command + " ", true, false, true); } catch { }
    }

    // ── 수명 ★[검토 0908·0909] 걷는 자리를 빠짐없이 ────────────────────────
    private static bool _hooked;

    /// <summary>도면 전환·닫기·배치 전환에 표시를 걷고, 자물쇠가 고착되지 않게 한다.
    ///
    /// <para>★★★<b><c>DocumentToBeDeactivated</c>다</b> — <c>DocumentActivated</c>가 아니다.
    /// 그것은 <b>이미 새 도면으로 넘어간 뒤</b>라, 그때 걷으면 새 도면의 임시 그래픽 관리자에게
    /// <b>옛 도면 객체</b>를 걷으라고 시키는 꼴이 된다.</para></summary>
    internal static void Hook()
    {
        if (_hooked) return;
        _hooked = true;
        try
        {
            var dm = AcadApp.DocumentManager;

            // 떠나기 <b>직전</b> — 옛 도면이 아직 현재라 제대로 걷힌다. 고른 것은 남긴다.
            dm.DocumentToBeDeactivated += (_, e) => { _nDocClear++; Unstick("도면 전환"); DropMarks(e.Document); };
            // 돌아왔다 — 살아 있는 것만 다시 칠한다.
            dm.DocumentActivated += (_, e) => Repaint(e.Document);
            // 완전히 닫힌다 — 관리자도 사라지므로 붙잡아 둔 것을 놓아 준다.
            // ★★★[검토 0909 · 치명] <c>ToBeDestroyed</c>는 <b>닫히기 전</b>이라 관리자가 아직 살아 있다 —
            //   여기서 걷는 것이 맞다. 종전에 위험했던 것은 <b>같은 핸들러가 방금 붙잡은 것을
            //   곧바로 <c>Dispose</c>했기 때문</b>이었다.
            //   ★이제 <see cref="PickMark.ReleaseFor"/>는 <b>목록에서 빼기만</b> 하고 해제하지 않으므로
            //     여기서 불러도 안전하다.
            //   ★<c>DocumentDestroyed</c>로 미루는 길은 못 쓴다 — 그 이벤트는 도면을 안 넘겨준다
            //     (이미 사라진 뒤라 <c>FileName</c>만 준다). 어느 도면 몫인지 가릴 수가 없다.
            dm.DocumentToBeDestroyed += (_, e) =>
            {
                _nDocClear++;
                Unstick("도면 닫기");
                ClearAll(e.Document);
                PickMark.ReleaseFor(e.Document);
            };
            dm.DocumentCreated += (_, e) => HookDoc(e.Document);
            foreach (AcDoc d in dm) HookDoc(d);
        }
        catch { _hooked = false; }
    }

    /// <summary>이 도면에 이미 걸었는가 — <b>도면에 매달아</b> 표시한다.</summary>
    private const string HookedKey = "DH.Grading.PickHooked";

    /// <summary>도면 하나에 거는 것 — 배치 전환과 <b>명령 끝</b>(자물쇠 감시견).</summary>
    private static void HookDoc(AcDoc doc)
    {
        if (doc == null) return;
        try
        {
            // ★★[검토 0909 · 높음] <b>이름으로 잠그면 안 된다.</b>
            //   <c>doc.Name</c>은 <b>저장 경로</b>다. <c>SAVEAS</c>를 하면 이름이 바뀌어
            //   정적 목록에 <b>옛 이름이 영영 남고</b>, 나중에 어떤 도면이 그 이름을 얻으면
            //   <b>훅을 아예 안 건다</b> — 그 도면에서는 감시견도 배치 훅도 없다.
            //   그러면 찍기가 비정상으로 끝났을 때 <b>자물쇠를 풀 길이 사라진다</b>(감시견을 넣은 이유가 무효).
            //   ★도면에 매달아 두면 이름이 바뀌어도 같이 산다 — <see cref="Bag"/>와 같은 방식이다.
            if (doc.UserData.Contains(HookedKey)) return;
            doc.UserData[HookedKey] = true;

            // ★[검토 0909 · 보통] <b>배치 전환</b>도 걷는 자리다 — 계획서가 적어 뒀는데 빠져 있었다.
            //   가상의 경로가 아니다: 도면 생성이 <b>애드인 스스로</b> 배치 탭으로 넘긴다.
            // ★★[검토 0909 · 높음] <b>명령이 도는 중에는 건드리지 않는다.</b>
            //   이 저장소는 배치를 <b>프로그램으로</b> 넘긴다(<c>SheetCommand</c>가 도곽마다).
            //   그때 여기가 돌면 <b>남의 트랜잭션 한복판에서</b> 임시 그래픽 수천 개를
            //   걷고 다시 만든다 — 도곽 20장이면 20번이다.
            doc.LayoutSwitching += (_, __) => { if (!_inCommand) DropMarks(doc); };
            doc.LayoutSwitched += (_, __) => { if (!_inCommand) Repaint(doc); };

            // ★[검토 0909 · 높음] <b>자물쇠 감시견.</b> 우리 찍기 명령이 어떻게 끝나든 자물쇠를 푼다.
            void Watch(string name)
            {
                _inCommand = false;
                if (name != null && name.StartsWith("DHPICK", System.StringComparison.OrdinalIgnoreCase)) End();
            }
            doc.CommandWillStart += (_, __) => _inCommand = true;
            doc.CommandEnded += (_, e) => Watch(e.GlobalCommandName);
            doc.CommandCancelled += (_, e) => Watch(e.GlobalCommandName);
            doc.CommandFailed += (_, e) => Watch(e.GlobalCommandName);
        }
        catch { }
    }
}

/// <summary>★★★[계획 3단계] <b>도킹창이 부르는 이름 있는 찍기 명령들.</b>
/// <para>창은 <see cref="PickSession.Send"/>로 부른다 — <c>^C^C</c>를 붙이는 규약이 그 안에 있다.</para></summary>
public sealed class PickCommands
{
    internal const string CmdPlan = "DHPICKPLAN";
    internal const string CmdGround = "DHPICKGROUND";
    internal const string CmdExcav = "DHPICKEXCAV";

    /// <summary>계획 경계(닫힌 폴리라인/3D폴리라인/피처라인)를 찍는다.</summary>
    [CommandMethod(CmdPlan, CommandFlags.Modal)]
    public static void PickPlan() => PickOne(PickSession.KeyPlan, "계획 경계",
        "\n계획 경계(닫힌 폴리라인/3D폴리라인/피처라인)를 선택: ", wantSurface: false);

    /// <summary>구조물 바닥 경계(터파기선)를 찍는다.</summary>
    [CommandMethod(CmdExcav, CommandFlags.Modal)]
    public static void PickExcav() => PickOne(PickSession.KeyExcav, "터파기선",
        "\n구조물 바닥 경계(닫힌 폴리라인/3D폴리라인/피처라인)를 선택: ", wantSurface: false);

    /// <summary>원지반 지표면을 찍는다.</summary>
    [CommandMethod(CmdGround, CommandFlags.Modal)]
    public static void PickGround() => PickOne(PickSession.KeyGround, "원지반",
        "\n원지반 지표면을 선택: ", wantSurface: true);

    /// <summary>고른 것을 전부 잊는다(빨간 표시도 걷는다). <b>자물쇠도 푼다</b> — 비상 탈출구.</summary>
    [CommandMethod("DHPICKCLEAR", CommandFlags.Modal)]
    public static void PickClear()
    {
        var doc = AcadApp.DocumentManager.MdiActiveDocument;
        if (doc == null) return;
        GradingSettings.SyncToDocument(doc);
        PickSession.ClearAll(doc);
        // ★[검토 0909 · 높음] 자물쇠가 고착됐을 때 <b>사용자가 쓸 수 있는 유일한 길</b>이다.
        PickSession.End();
        doc.Editor.WriteMessage("\n[찍기] 고른 것을 모두 지웠습니다(찍기 자물쇠도 풀었습니다).");
    }

    /// <summary>지금 무엇이 골라져 있나 — <b>창이 없는 동안 손으로 확인</b>하는 길.</summary>
    [CommandMethod("DHPICKSTATUS", CommandFlags.Modal)]
    public static void PickStatus()
    {
        var doc = AcadApp.DocumentManager.MdiActiveDocument;
        if (doc == null) return;
        GradingSettings.SyncToDocument(doc);
        PickSession.Hook();                 // ★한 번도 안 찍었어도 수명 관리는 걸려 있어야 한다
        var ed = doc.Editor;
        ed.WriteMessage("\n[찍기] 지금 고른 것:");
        foreach (var key in new[] { PickSession.KeyPlan, PickSession.KeyGround, PickSession.KeyExcav })
        {
            var s = PickSession.Peek(doc, key);
            // ★<b>보기만 한다</b> — 여기서 지우면 되돌리기로 살린 것도 못 돌아온다(검토 0909).
            var id = PickSession.TryResolve(doc, key, out bool gone);
            ed.WriteMessage(s == null ? $"\n  · {key} — 미선택"
                          : gone ? $"\n  · {key} — {s.What} ⚠지금은 도면에 없습니다(되돌리면 살아납니다)"
                          : $"\n  · {key} — {s.What} (핸들 {s.Handle})");
        }
        ed.WriteMessage($"\n  {PickSession.Tally}");
    }

    /// <summary>하나 찍는 일의 뼈대 — <b>어떤 길로 빠져나가도 자물쇠를 푼다</b>.</summary>
    private static void PickOne(string key, string what, string prompt, bool wantSurface)
    {
        var doc = AcadApp.DocumentManager.MdiActiveDocument;
        if (doc == null) return;
        var ed = doc.Editor;
        // ★[검토 0909 · 보통] 다른 명령들과 같은 규약 — 이제 <b>이것이 도킹창의 명령 문맥 진입점</b>이다.
        try { GradingSettings.SyncToDocument(doc); } catch { }
        PickSession.Hook();
        if (!PickSession.Begin(ed, what)) return;
        try
        {
            var peo = new PromptEntityOptions(prompt);
            if (wantSurface)
            {
                peo.SetRejectMessage("\n지표면(TIN Surface)이어야 합니다.");
                // ★[검토 0909 · 낮음] 기존 지표면 선택 다섯 곳이 전부 <c>true</c>다 — 선례에 맞춘다.
                peo.AddAllowedClass(typeof(Autodesk.Civil.DatabaseServices.TinSurface), true);
            }
            else
            {
                peo.SetRejectMessage("\n폴리라인 또는 피처라인이어야 합니다.");
                peo.AddAllowedClass(typeof(Polyline), false);
                peo.AddAllowedClass(typeof(Polyline3d), false);
                peo.AddAllowedClass(typeof(Autodesk.Civil.DatabaseServices.FeatureLine), false);
            }
            var r = ed.GetEntity(peo);
            if (r.Status != PromptStatus.OK)
            {
                ed.WriteMessage($"\n[찍기] {what} — 취소했습니다(고르기 전 상태 그대로).");
                return;
            }

            string name = what;
            try
            {
                using var tr = doc.Database.TransactionManager.StartTransaction();
                var o = tr.GetObject(r.ObjectId, OpenMode.ForRead);
                name = o is Autodesk.Civil.DatabaseServices.TinSurface ts ? ts.Name : o.GetType().Name;
                tr.Commit();
            }
            catch { }

            // ★★[검토 0909 · 높음] <b>지표면은 칠할 수 없다 — 칠했다고 말하지도 않는다.</b>
            //   <see cref="BoundaryReader"/>는 폴리선 셋만 읽고 그 외는 던진다. 그래서 지표면을 고를 때마다
            //   표시는 안 되면서 <b>"빨갛게 표시했습니다"</b>가 찍히고 진단 로그에 "표시 실패"가 쌓였다 —
            //   조용한 실패의 반대판, <b>시끄러운 거짓 성공</b>이다.
            PickSession.Set(doc, key, r.ObjectId, name, paint: !wantSurface);
            ed.WriteMessage(wantSurface
                ? $"\n[찍기] {what} = {name} — 골랐습니다(면은 색으로 표시하지 않습니다)."
                : $"\n[찍기] {what} = {name} — 빨갛게 표시했습니다.");
        }
        catch (System.Exception ex)
        {
            // ★명령 안에서 잡는다 — 여기서 안 잡으면 프로세스가 죽는다(이 방식을 고른 이유 중 하나).
            ed.WriteMessage($"\n[찍기] {what} 실패 — {ex.Message}");
            try { DiagLog.Append($"\n■ 찍기 예외({what}) — {ex.GetType().Name}: {ex.Message}\n"); } catch { }
        }
        finally { PickSession.End(); }
    }
}
