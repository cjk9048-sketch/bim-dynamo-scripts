using Autodesk.AutoCAD.ApplicationServices;
using DH.Grading.Core;

namespace DH.Grading.Civil;

/// <summary>★★★[v103.1.12 · JACK 1014 «합성 미루기» · 1015 ② «최종면이 필요한 일 직전 합성이 끝내 안 되면 물어보기 — 옹벽이 빠진 정지면으로 계속할까요?(기본 아니오)»] <b>합성 관문</b>.
/// <para>정지면_DH(옹벽 합성)를 읽는 일 — InfraWorks 내보내기 · 계획면 기준 터파기(세 길 모두 DoExcav 안) · 계획부지 생성 «이어서»(합성이 새 구역 기준면에 굳는다) — 바로 앞에서 부른다.
/// 셈은 Core <see cref="WallList.CompositeState"/>(하네스 S158) · 합성은 <see cref="Commands.WallCompositeCommand.RunFor"/>(명령과 같은 RunInner).</para>
/// <para>빠진 옹벽이 없으면 아무것도 안 한다(옵션을 끈 보통 흐름 — 합성이 늘 끝까지 가므로). 합성할 것이 있으면 한 번 합성하고(같은 상태로 이미 해 봤으면 건너뜀 — 세션 기억),
/// 그래도 빠졌으면 묻는다. 끔에서도 움직인 보류 · 남은 «대기»가 있으면 묻는다(지금까지는 말없이 옹벽 빠진 면으로 나갔다 — 4판 중간 4).</para>
/// <para>명령 안 · 트랜잭션 밖에서 부른다(합성이 제 트랜잭션 · 문서 잠금을 쓴다).</para></summary>
internal static class CompositeGate
{
    /// <summary>화면 없는 Civil 시험 칸 — 묻는 창 대신 이 답(없으면 «아니오»).</summary>
    internal static bool? AnswerForTest;
    /// <summary>도면마다 «이 상태로 합성해 봤는데 안 됐다»(목록 지문) — 같은 상태면 다시 안 해 본다(내보내기를 여러 번 눌러도 매번 수십 초씩 안 쓴다).</summary>
    static readonly System.Collections.Generic.Dictionary<string, string> _tried = new(System.StringComparer.OrdinalIgnoreCase);
    /// <summary>★[4판 재검토 H4 b] 마지막으로 «예»(빠진 채로 계속)를 받은 도면 · 번호 — «이어서»가 이 번호만 목록에서 지운다(묻지 않았으면 없음).</summary>
    static (string Doc, System.Collections.Generic.HashSet<int> Nos)? _lastYes;

    /// <summary>★[v103.2] 꺼내지 않고 본다 — «이어서» 들머리가 굳을 옹벽 수를 셀 때(꺼내는 것은 3단계).</summary>
    internal static System.Collections.Generic.HashSet<int> PeekYes(string docName)
    {
        var y = _lastYes;
        return y != null && string.Equals(y.Value.Doc, docName, System.StringComparison.OrdinalIgnoreCase) ? new(y.Value.Nos) : new();
    }

    /// <summary>«이어서»가 꺼내 쓴다(꺼내면 비운다) — 이 도면에서 «예»를 받은 빠진 옹벽 번호 · 없으면 빈 집합.</summary>
    internal static System.Collections.Generic.HashSet<int> TakeYes(string docName)
    {
        var y = _lastYes; _lastYes = null;
        return y != null && string.Equals(y.Value.Doc, docName, System.StringComparison.OrdinalIgnoreCase) ? y.Value.Nos : new();
    }

    /// <param name="caller">부르는 일(명령줄 · 묻는 창 제목 · 자가점검).</param>
    /// <param name="dropNote">«예»를 고르면 빠진 옹벽이 어떻게 되는지(«이어서»: 목록에서 빠진다).</param>
    /// <returns>계속해도 되나 — 거짓이면 부르는 쪽은 아무것도 안 바꾸고 멈춘다.</returns>
    internal static bool Ensure(Document doc, string caller, string dropNote = "")
    {
        var db = doc.Database; var ed = doc.Editor;
        var sw = System.Diagnostics.Stopwatch.StartNew();
        _lastYes = null;
        WallList.CompState? st; string why, p0; int[]? tag; System.Collections.Generic.List<WallRec> ws;
        bool Load()
        {
            try
            {
                using var tr = db.TransactionManager.StartTransaction();
                st = Commands.WallCompositeCommand.StateNow(db, tr, out why, out p0, out tag, out ws);
                tr.Commit();
                return true;
            }
            catch (System.Exception ex) { st = null; why = ex.GetType().Name + ": " + ex.Message; p0 = ""; tag = null; ws = new(); return false; }
        }
        Load();
        if (st == null)
        {
            // 목록을 못 읽었다(더 새 판이 쓴 목록 등) — 무엇이 빠졌는지 모른다 · 종전처럼 그대로 간다(로그에 남긴다)
            Log(caller, $"옹벽 목록을 못 읽어 합성 관문을 못 봤다 — 그대로 진행({why})");
            SelfCheckLog.Line($"합성 관문 — {caller}", '–', $"목록을 못 읽어 못 봄 — 그대로 진행({why})", doc.Name);
            return true;
        }
        if (st.Missing.Count == 0)
        {
            Log(caller, $"빠진 옹벽 없음 — {st.Text}");
            return true;
        }
        string ran = "", lost = "";
        if (st.Need)
        {
            string sig = WallList.StateSig(ws, p0, tag);
            if (_tried.TryGetValue(doc.Name, out var s0) && s0 == sig) ran = "같은 상태로 이미 합성해 봤다 — 다시 안 함";
            else
            {
                try { ed.WriteMessage($"\n[{caller}] 정지면_DH에 빠진 옹벽 {st.Missing.Count}개 — 먼저 합성합니다(옹벽 하나에 약 20초)…"); } catch { }
                (bool ok, string sum) = (false, "");
                // ★[v105.1.1 · JACK 1023] 합성 동안 로딩 창(종단이 띄운 창이 있으면 그 창을 그대로 쓴다)
                using (BusyPopup.Scope(Commands.CreateGradingCommand.BusyText))
                    (ok, sum) = Commands.WallCompositeCommand.RunFor(doc, caller);
                ran = (ok ? "합성 됨 — " : "합성 못 함 — ") + SelfCheckLog.Clean(sum);
                // ★[4판 재검토 새 높음 1] 그 합성이 옹벽을 버렸거나(대기) 뺐으면(면이 없어진 넣음) 목록에서 사라져 «빠짐»에 안 잡힌다 — 그래도 묻는다(명령줄 한 줄로 묻히지 않게)
                lost = Commands.WallCompositeCommand.LastLost;
                Load();
                if (st != null && st.Missing.Count == 0 && lost.Length == 0)
                {
                    _tried.Remove(doc.Name);
                    Log(caller, $"빠진 옹벽을 합성해 다 넣었다({sw.ElapsedMilliseconds / 1000.0:F1}초) · {ran}");
                    SelfCheckLog.Line($"합성 관문 — {caller}", true, $"빠진 옹벽을 먼저 합성해 다 넣었다 · {sw.ElapsedMilliseconds / 1000.0:F1}초", doc.Name);
                    return true;
                }
                if (st != null) _tried[doc.Name] = WallList.StateSig(ws, p0, tag);
            }
        }
        if (st == null) { Log(caller, "합성 뒤 목록을 못 읽었다 — 멈춤"); return false; }

        // ── 묻기(기본 아니오) ──
        var lines = st.Missing.Take(5).Select(n => Describe(n, ws)).ToList();
        string more = st.Missing.Count > 5 ? $"\n외 {st.Missing.Count - 5}개" : "";
        string msg = (st.Missing.Count > 0 ? $"옹벽 {st.Missing.Count}개가 정지면_DH에 안 들어가 있습니다.\n\n" + string.Join("\n", lines) + more + "\n\n" : "")
                   + (lost.Length > 0 ? $"방금 합성하면서 옹벽을 뺐습니다(목록에서 지워짐):\n{lost}\n\n" : "")
                   + "옹벽이 빠진 정지면으로 계속할까요?" + (dropNote.Length > 0 && st.Missing.Count > 0 ? $"\n({dropNote})" : "") + "\n\n"
                   + "예: 빠진 채로 계속합니다\n아니오: 멈춥니다" + (lost.Length > 0 ? "(뺀 옹벽은 돌아오지 않습니다 — 옹벽 변환을 다시 하세요)" : "(아무것도 안 바뀝니다)");
        bool yes;
        if (SelfCheckLog.Headless) yes = AnswerForTest ?? false;   // 화면 없는 Civil은 창을 못 띄운다(띄우면 죽는다)
        else
        {
            var ans = System.Windows.MessageBox.Show(msg, $"DH 정지 — {caller}", System.Windows.MessageBoxButton.YesNo,
                                                     System.Windows.MessageBoxImage.Warning, System.Windows.MessageBoxResult.No);
            yes = ans == System.Windows.MessageBoxResult.Yes;
        }
        string body = $"빠진 옹벽 {(st.Missing.Count == 0 ? "0" : string.Join(",", st.Missing))}({string.Join(" · ", lines)}){(lost.Length > 0 ? " · 합성이 뺀 옹벽: " + SelfCheckLog.Clean(lost) : "")}{(ran.Length > 0 ? " · " + ran : "")} → {(yes ? "예 — 빠진 채로 계속" : "아니오 — 멈춤")} · {st.Text}";
        Log(caller, body);
        SelfCheckLog.Line($"합성 관문 — {caller}", false, body, doc.Name);
        if (yes) _lastYes = (doc.Name, new System.Collections.Generic.HashSet<int>(st.Missing));
        try
        {
            string missTxt = st.Missing.Count > 0 ? "옹벽 " + string.Join(",", st.Missing) : "합성이 뺀 옹벽";
            ed.WriteMessage(yes ? $"\n[{caller}] ⚠{missTxt}이 빠진 정지면_DH로 계속합니다."
                                : $"\n[{caller}] 멈췄습니다 — {missTxt}이 정지면_DH에 없습니다(정지 창 [옹벽 합성] · 진단 로그).");
        }
        catch { }
        return yes;
    }

    /// <summary>빠진 옹벽 한 줄 — 무엇 때문에 빠졌나.</summary>
    static string Describe(int no, System.Collections.Generic.List<WallRec> ws)
    {
        var w = ws.FirstOrDefault(x => x.No == no);
        if (w == null) return $"옹벽 {no} — 합성 표지에만 남은 번호(목록에 없다)";
        string tagS = $"옹벽 {no}({w.Side} {w.Bench + 1}단)";
        if (w.State == 0) return $"{tagS} — 합성 전 «대기»";
        if (w.Hold.Length > 0) return $"{tagS} — 보류: {SelfCheckLog.Clean(WallList.IsMovedHold(w.Hold) ? w.Hold : WallList.HoldWhy(w.Hold))}";
        if (WallList.IsDeferred(w)) return $"{tagS} — 합성을 미뤄 둠";
        return $"{tagS} — 지금 정지면_DH 합성에 안 들어 있음";
    }

    static void Log(string caller, string s)
    {
        try { DiagLog.Append($"\n■ {System.DateTime.Now:HH:mm:ss} 합성 관문({caller}) — {s}\n"); } catch { }
    }
}
