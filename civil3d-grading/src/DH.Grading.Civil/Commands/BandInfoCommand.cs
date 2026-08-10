using System;
using System.Collections;
using System.Reflection;
using System.Text;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.EditorInput;
using Autodesk.AutoCAD.Runtime;
using AcadApp = Autodesk.AutoCAD.ApplicationServices.Application;
using CivilApp = Autodesk.Civil.ApplicationServices;

namespace DH.Grading.Civil.Commands;

/// <summary>★[JACK 0810] <b>밴드 검토용 진단 — 도면을 전혀 건드리지 않는다(읽기 전용).</b>
///
/// <para>JACK: "일단 있는 밴드를 덮기 전에 그 밴드 스타일 자체가 적정한지부터 검토하고,
/// 아니다 싶으면 새로 정의해서 만들고 싶어. 너무 급조해서 만든 거긴 하거든?"</para>
///
/// <para>맞는 순서다. 급조된 것을 자동화하면 <b>틀린 것을 빠르게 반복</b>할 뿐이다.
/// 그런데 밴드 세트 안에 밴드가 몇 장 어떤 순서로 들어 있는지, 각 밴드가 눈금·라벨을 어디에
/// 찍게 되어 있는지는 <b>파일 밖에서 보이지 않는다</b>. 모르는 채로 '적정하다/아니다'를 말하면
/// 짐작이 된다 — 이 저장소가 스타일 심기에서 세 판을 날린 이유가 정확히 그것이었다.</para>
///
/// <para><b>반사(reflection)로 통째로 찍는 이유.</b> 속성 이름을 코드에 박으면 하나만 달라도
/// 컴파일이 깨지거나 조용히 빠진다. 여기서는 '무엇이 있는지'를 알아내는 게 목적이므로
/// 공개 속성을 전부 훑어 값을 그대로 남긴다. 값이 ObjectId면 그 객체의 이름까지 풀어 준다.</para></summary>
public static class BandInfoCommand
{
    [CommandMethod("DHBANDINFO", CommandFlags.Modal)]
    public static void Run()
    {
        var doc = AcadApp.DocumentManager.MdiActiveDocument;
        if (doc == null) return;
        var db = doc.Database;
        Editor ed = doc.Editor;
        var log = new StringBuilder();
        int nSet = 0, nBand = 0;

        try
        {
            var cdoc = CivilApp.CivilApplication.ActiveDocument;
            using var tr = db.TransactionManager.StartTransaction();

            // ── ① 밴드 '세트' 스타일 — 정보표시 테이블 한 벌이 어떻게 짜여 있는가
            log.AppendLine("── 밴드 세트 스타일(정보표시 테이블) ──");
            foreach (var s in ProfileStyleTemplate.Collect(db, cdoc, x => x.Cls.Contains("BandStyleSet")))
            {
                nSet++;
                log.AppendLine($"\n■ 세트 '{s.Name}'  [{s.Cls}]  @{s.Path}");
                object? st = null;
                try { st = tr.GetObject(s.Id, OpenMode.ForRead); } catch (System.Exception ex) { log.AppendLine("   열기실패: " + ex.Message); }
                if (st == null) continue;
                // 세트가 담고 있는 밴드 목록 — 메서드 이름을 박지 않고 Get*BandSetItems 류를 반사로 찾아 부른다.
                foreach (var m in st.GetType().GetMethods(BindingFlags.Public | BindingFlags.Instance))
                {
                    if (m.GetParameters().Length != 0) continue;
                    if (!m.Name.StartsWith("Get", StringComparison.Ordinal) || !m.Name.Contains("Band")) continue;
                    object? col;
                    try { col = m.Invoke(st, null); }
                    catch (System.Exception ex) { log.AppendLine($"   {m.Name}() → 예외 {Inner(ex)}"); continue; }
                    if (col is not IEnumerable en) { log.AppendLine($"   {m.Name}() → {col}"); continue; }
                    int k = 0;
                    foreach (var item in en)
                    {
                        log.AppendLine($"   [{m.Name} {k++}] {item?.GetType().Name}");
                        Dump(item, tr, log, "      ");
                    }
                    if (k == 0) log.AppendLine($"   {m.Name}() → 비어 있음");
                    if (col is IDisposable dsp) { try { dsp.Dispose(); } catch { } }
                }
            }

            // ── ② 밴드 스타일 낱장 — 눈금·라벨을 어디에 찍게 되어 있는가
            log.AppendLine("\n\n── 밴드 스타일 낱장 ──");
            foreach (var s in ProfileStyleTemplate.Collect(db, cdoc,
                         x => x.Cls.Contains("Band") && !x.Cls.Contains("BandStyleSet")))
            {
                nBand++;
                log.AppendLine($"\n■ '{s.Name}'  [{s.Cls}]  @{s.Path}");
                try { Dump(tr.GetObject(s.Id, OpenMode.ForRead), tr, log, "   "); }
                catch (System.Exception ex) { log.AppendLine("   열기실패: " + ex.Message); }
            }

            tr.Commit();   // 읽기만 했다 — 도면은 바뀌지 않는다
        }
        catch (System.Exception ex) { log.AppendLine("\n⚠중단: " + ex.Message); }

        try { DiagLog.Append("\n■ DHBANDINFO(밴드 검토)\n" + log + "\n"); } catch { }
        ed.WriteMessage($"\n[밴드검토] 세트 {nSet}벌 · 밴드 스타일 {nBand}종 기록" +
                        $"\n  도면은 건드리지 않았습니다(읽기 전용)." +
                        $"\n  자세한 내용: {DiagLog.FilePath}");
    }

    /// <summary>공개 속성을 전부 훑어 값을 남긴다. 무엇이 있는지 알아내는 게 목적이라 이름을 박지 않는다.</summary>
    private static void Dump(object? o, Transaction tr, StringBuilder sb, string ind)
    {
        if (o == null) { sb.AppendLine(ind + "(null)"); return; }
        foreach (var p in o.GetType().GetProperties(BindingFlags.Public | BindingFlags.Instance))
        {
            if (p.GetIndexParameters().Length > 0 || !p.CanRead) continue;
            string v;
            try { v = Fmt(p.GetValue(o), tr); }
            catch (System.Exception ex) { v = "<예외:" + Inner(ex) + ">"; }   // 못 읽는 속성은 '왜'까지 남긴다
            sb.AppendLine($"{ind}{p.Name} = {v}");
        }
        // 기하점 선택(수평/수직) 같은 것은 속성이 아니라 Get…() 메서드로 나온다.
        foreach (var m in o.GetType().GetMethods(BindingFlags.Public | BindingFlags.Instance))
        {
            if (m.GetParameters().Length != 0 || m.ReturnType == typeof(void)) continue;
            if (!m.Name.StartsWith("Get", StringComparison.Ordinal)) continue;
            if (m.Name is "GetType" or "GetHashCode") continue;
            string v;
            try { v = Fmt(m.Invoke(o, null), tr); }
            catch (System.Exception ex) { v = "<예외:" + Inner(ex) + ">"; }
            sb.AppendLine($"{ind}{m.Name}() = {v}");
        }
    }

    /// <summary>값을 사람이 읽을 수 있게. ObjectId는 그 객체의 이름까지 풀어 준다 — 숫자만으론 못 읽는다.</summary>
    private static string Fmt(object? v, Transaction tr)
    {
        switch (v)
        {
            case null: return "(null)";
            case ObjectId id:
                if (id.IsNull) return "(없음)";
                try
                {
                    var ob = tr.GetObject(id, OpenMode.ForRead);
                    string nm = "";
                    var pn = ob.GetType().GetProperty("Name");
                    if (pn != null) { try { nm = pn.GetValue(ob) as string ?? ""; } catch { } }
                    return $"'{nm}' [{id.ObjectClass?.Name}]";
                }
                catch { return $"<{id.ObjectClass?.Name ?? "?"}>"; }
            case string s: return "\"" + s + "\"";
            case IEnumerable en and not string:
                var parts = new System.Collections.Generic.List<string>();
                try { foreach (var e in en) { parts.Add(Fmt(e, tr)); if (parts.Count >= 20) { parts.Add("…"); break; } } }
                catch (System.Exception ex) { parts.Add("<예외:" + Inner(ex) + ">"); }
                return "[" + string.Join(", ", parts) + "]";
            default:
                var t = v.GetType();
                if (t.IsEnum || t.IsPrimitive || v is decimal) return v.ToString() ?? "";
                // 값 묶음(구조체 등)은 한 겹만 더 펼친다
                if (t.Namespace != null && t.Namespace.StartsWith("Autodesk", StringComparison.Ordinal))
                {
                    var inner = new StringBuilder(t.Name + "{");
                    foreach (var p in t.GetProperties(BindingFlags.Public | BindingFlags.Instance))
                    {
                        if (p.GetIndexParameters().Length > 0 || !p.CanRead) continue;
                        object? iv; try { iv = p.GetValue(v); } catch { continue; }
                        if (iv is ObjectId || iv is string || (iv?.GetType().IsPrimitive ?? false) || (iv?.GetType().IsEnum ?? false))
                            inner.Append($" {p.Name}={(iv is ObjectId oid2 ? Fmt(oid2, tr) : iv)}");
                    }
                    return inner.Append(" }").ToString();
                }
                return v.ToString() ?? "";
        }
    }

    private static string Inner(System.Exception ex)
        => (ex.InnerException?.Message ?? ex.Message).Replace("\r", " ").Replace("\n", " ");
}
