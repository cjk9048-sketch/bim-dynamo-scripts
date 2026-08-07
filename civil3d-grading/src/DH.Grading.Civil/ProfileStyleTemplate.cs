using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.Civil.ApplicationServices;
using Autodesk.Civil.DatabaseServices.Styles;

namespace DH.Grading.Civil;

/// <summary>★[JACK 0807 2단계] 회사 표준 종단도 스타일을 <b>DLL에 심어</b> 도면에 자동으로 들여온다.
///
/// <para><b>왜 심는가.</b> 종단도를 뽑아도 도면에 회사 스타일이 없으면 Civil 3D 기본 스타일로 그려져,
/// 사람이 매번 Toolspace에서 스타일을 손으로 들여오고 골라 줘야 했다 — 손이 두 번 가고, 빼먹으면
/// 도면 모양이 사람마다 달라진다. 템플릿(DHT.dwt)을 DLL 안에 넣어 두면 dwt를 챙기거나 경로를
/// 맞출 필요가 없다.</para>
///
/// <para><b>세 번 실패하고 알아낸 것(v21.5~v21.8) — 재시도 금지 목록.</b>
/// Civil 스타일은 도면의 '명명된 객체 사전(NOD)'에 들어 있는 평범한 객체가 <b>아니다</b>.
/// <c>AeccDbTreeOid</c>라는 별도의 나무 구조에 매달려 있다. 그래서:</para>
/// <list type="number">
/// <item><b>WblockCloneObjects는 원리상 불가.</b> 그 API는 '붙일 자리(owner ObjectId)'를 요구하는데
///   Civil 스타일에는 그런 owner가 없다 — 어떤 값을 줘도 <c>eInvalidOwnerObject</c>다(v21.5).
///   호출 방향도 트랜잭션 타이밍도 옳았다. 자리를 더 잘 고르면 될 문제가 아니다.</item>
/// <item><b>NOD 재귀 순회도 원리상 불가.</b> 곁 도면에서 <c>Root</c>가 <c>DBObject</c>로 열리는 건
///   증상이 아니라 당연한 결과다 — 애초에 사전이 아니기 때문이다(v21.7·v21.8).</item>
/// <item><b>WorkingDatabase 바꿔치기는 절대 금지.</b> 진짜 도면의 지표면을 Civil이 못 찾게 되어
///   종단이 통째로 안 만들어졌다(v21.6). 게다가 필요도 없다 — <c>CivilDocument.get_Styles</c>는
///   '넘겨받은 Database' 기준으로 동작하지 WorkingDatabase와 무관하다.</item>
/// </list>
///
/// <para><b>정답.</b> Civil 3D가 스타일 전용 크로스-도면 복사 API를 공식 제공한다:
/// <c>StyleBase.ExportTo(ObjectIdCollection, Database, StyleConflictResolverType)</c>.
/// '서로 다른 도면 사이의 복사' 전용으로 설계돼 있어(같은 도면이면 예외를 던진다) 붙일 자리를
/// 지목할 필요가 없다 — 종류별 스타일 뿌리를 스스로 찾아 붙이고 딸린 스타일도 함께 끌고 온다.</para>
///
/// <para><b>이름을 추측하지 않는다.</b> 종전엔 가져올 스타일 이름을 파일 바이트에서 눈으로 읽어
/// 코드에 박아 뒀는데, 공백 한 칸만 달라도 못 찾고 실제로 이름이 다를 수도 있었다.
/// 이제 템플릿의 스타일을 <b>전부 열거해 이름을 읽고</b>, 'DH'로 시작하는 것을 통째로 가져온다.
/// 회사 표준은 어차피 다 필요하고, 이름 오타 문제가 통째로 사라진다.</para>
///
/// <para><b>이름으로 묻지 않고 열거한다.</b> <c>Contains(name)</c>는 네이티브
/// <c>hasChild</c>(직계 자식 정확일치)를 부르는 반면 <c>Count</c>/인덱서는
/// <c>getAllChildren</c>을 부른다 — 서로 다른 목록이다. 열거가 진실에 가깝다.</para></summary>
public static class ProfileStyleTemplate
{
    /// <summary>가져올 스타일 이름의 접두어 — 회사 표준은 전부 이걸로 시작한다.</summary>
    private const string Prefix = "DH";

    /// <summary>종단 뷰 스타일의 RX 클래스(격자·표고축·제목).</summary>
    public const string ClsProfileView = "AeccDbGraphStyleProfile";
    /// <summary>★핵심 — <b>종단 데이터</b> 밴드. 원지반/계획 표고·누가거리를 채우는 밴드가 이것이다.
    /// (이름에 '횡단 데이터'가 들어간 밴드는 <c>AeccDbGraphStyleSectionalDataBands</c>로 <b>종류가 다르고</b>,
    /// 그 밴드는 Profile1/Profile2를 쓰지 않아 표고 칸이 비어 있게 된다 — 이름이 아니라 종류로 골라야 한다.)</summary>
    public const string ClsProfileDataBand = "AeccDbGraphStyleProfileDataBands";

    /// <summary>직전 <see cref="Import"/> 요약 — 명령창에 찍는다.</summary>
    public static string LastReport { get; private set; } = "";

    /// <summary>직전 <see cref="Import"/>의 상세(가져온 스타일의 이름·종류·자리) — 로그 파일 전용.
    /// 다음 단계(밴드 종류 확정)의 근거가 되므로 반드시 남긴다.</summary>
    public static string LastProbe { get; private set; } = "";

    private static string? _dwtPath;

    /// <summary>스타일 하나에 대한 실측 정보.</summary>
    public readonly record struct StyleInfo(ObjectId Id, string Name, string Cls, string Path);

    /// <summary>심어 둔 dwt를 임시폴더에 푼다(한 세션에 한 번).</summary>
    private static string? ExtractTemplate(out string why)
    {
        why = "";
        if (_dwtPath != null && File.Exists(_dwtPath)) return _dwtPath;
        try
        {
            var asm = Assembly.GetExecutingAssembly();
            string? res = asm.GetManifestResourceNames()
                             .FirstOrDefault(n => n.EndsWith("DHT.dwt", StringComparison.OrdinalIgnoreCase));
            if (res == null) { why = "DLL에 DHT.dwt 리소스가 없음"; return null; }
            string dir = Path.Combine(Path.GetTempPath(), "DH.Grading");
            Directory.CreateDirectory(dir);
            string path = Path.Combine(dir, "DHT.dwt");
            using (var s = asm.GetManifestResourceStream(res))
            {
                if (s == null) { why = "리소스 스트림 열기 실패"; return null; }
                using var f = File.Create(path);
                s.CopyTo(f);
            }
            _dwtPath = path;
            return path;
        }
        catch (Exception ex) { why = "추출 실패: " + ex.Message; return null; }   // 예외를 삼키지 않는다
    }

    /// <summary>이름 비교용 정규화 — 앞뒤 공백·줄바꿈 없는 공백(NBSP)·전각 공백이 섞여 있으면
    /// 눈으로는 같은데 정확일치가 실패한다.</summary>
    private static string Norm(string s)
        => s.Replace(' ', ' ').Replace('　', ' ').Trim();

    /// <summary>도면의 Civil 스타일을 <b>전부 열거</b>해 이름·종류를 읽는다.
    /// 이름으로 묻지(Contains) 않고 열거하는 이유는 클래스 주석 참조.</summary>
    public static List<StyleInfo> Collect(Database db, CivilDocument doc, Func<StyleInfo, bool>? keep)
    {
        var res = new List<StyleInfo>();
        var seen = new HashSet<ObjectId>();
        var map = new Dictionary<string, StyleCollectionBase>();
        Walk(doc.Styles, "", 0, map);
        try
        {
            using var tr = db.TransactionManager.StartTransaction();
            foreach (var kv in map)
            {
                int n;
                try { n = kv.Value.Count; } catch { continue; }
                for (int i = 0; i < n; i++)
                {
                    // 한 개가 실패해도 루프 전체를 끊지 않는다 — v21.5가 정확히 이 지점에서 전멸했다.
                    try
                    {
                        ObjectId sid = kv.Value[i];
                        if (sid.IsNull || !seen.Add(sid)) continue;
                        string cls = ""; try { cls = sid.ObjectClass.Name; } catch { }
                        if (tr.GetObject(sid, OpenMode.ForRead) is not StyleBase sb) continue;
                        string nm; try { nm = sb.Name; } catch { continue; }
                        if (string.IsNullOrEmpty(nm)) continue;
                        var si = new StyleInfo(sid, nm, cls, kv.Key);
                        if (keep == null || keep(si)) res.Add(si);
                    }
                    catch { }
                }
            }
            tr.Commit();
        }
        catch { }
        return res;
    }

    /// <summary>템플릿의 'DH' 스타일을 현재 도면으로 가져온다. 반환=가져온 개수.</summary>
    public static int Import(Database dstDb, CivilDocument dstCivil)
    {
        LastProbe = "";
        int okCount = 0;
        var fail = new List<string>();
        try
        {
            string? dwt = ExtractTemplate(out string exWhy);
            if (dwt == null) { LastReport = "스타일: 템플릿을 못 꺼냄 — " + exWhy; return 0; }

            // 곁 도면을 연다. WorkingDatabase는 절대 건드리지 않는다(v21.6 참사).
            using var srcDb = new Database(false, true);
            srcDb.ReadDwgFile(dwt, FileShare.Read, allowCPConversion: true, password: null);
            srcDb.CloseInput(true);
            var srcCivil = CivilDocument.GetCivilDocument(srcDb);

            var src = Collect(srcDb, srcCivil, si => Norm(si.Name).StartsWith(Prefix, StringComparison.Ordinal));
            if (src.Count == 0)
            {
                // 하나도 못 찾았으면 **템플릿에 실제로 뭐가 들었는지** 통째로 남긴다 — 다음 판의 근거.
                var allS = Collect(srcDb, srcCivil, null);
                LastProbe = "  템플릿 스타일 전수(최대 120개):\n    "
                          + string.Join("\n    ", allS.Take(120).Select(s => $"[{s.Cls}] {s.Name}  @{s.Path}"));
                LastReport = $"스타일: 들여옴 0개 — 템플릿에서 '{Prefix}' 스타일을 못 찾음(전체 {allS.Count}개 확인, 목록은 로그)";
                return 0;
            }

            // 이미 도면에 있는 이름은 건너뛴다 — 사용자가 손본 스타일을 덮어쓰지 않는다.
            var here = new HashSet<string>(
                Collect(dstDb, dstCivil, null).Select(s => Norm(s.Name)), StringComparer.Ordinal);
            var todo = src.Where(s => !here.Contains(Norm(s.Name))).ToList();
            int had = src.Count - todo.Count;
            if (todo.Count == 0)
            {
                LastReport = $"스타일: 들여옴 0개 · 이미있음 {had}개(도면에 이미 회사 표준이 들어 있음)";
                return 0;
            }

            // ── 공식 API로 복사. 일괄이 실패하면 하나씩 다시 — AutoCAD 일괄 API는 전부 아니면 전무라,
            //    한 개가 걸리면 나머지 멀쩡한 것까지 못 들어온다(이 저장소의 확립된 처방).
            try
            {
                var ids = new ObjectIdCollection();
                foreach (var s in todo) ids.Add(s.Id);
                StyleBase.ExportTo(ids, dstDb, Autodesk.Civil.StyleConflictResolverType.Ignore);
                okCount = todo.Count;
            }
            catch (Exception exAll)
            {
                fail.Add("일괄실패:" + exAll.Message);
                foreach (var s in todo)
                {
                    try
                    {
                        StyleBase.ExportTo(new ObjectIdCollection { s.Id }, dstDb,
                                           Autodesk.Civil.StyleConflictResolverType.Ignore);
                        okCount++;
                    }
                    catch (Exception ex) { fail.Add($"{s.Name}:{ex.Message}"); }
                }
            }

            LastProbe = "  가져온 스타일(이름 · 종류 · 자리):\n    "
                      + string.Join("\n    ", todo.Select(s => $"{s.Name}  [{s.Cls}]  @{s.Path}"));
            LastReport = $"스타일: 들여옴 {okCount}/{todo.Count}개"
                       + (had > 0 ? $" · 이미있음 {had}개" : "")
                       + (fail.Count > 0 ? $" · 실패[{string.Join(" | ", fail.Take(4))}]" : "");
            return okCount;
        }
        catch (Exception ex)
        {
            LastReport = $"스타일: 들여옴 {okCount}개 · 예외:{ex.Message}";
            return okCount;
        }
    }

    /// <summary>현재 도면에서 <b>종류(RX 클래스)로</b> 스타일을 고른다 — 이름이 아니라 종류로 골라야
    /// '이름은 그럴듯한데 종류가 달라 값이 안 채워지는' 함정을 피한다.
    /// 같은 종류가 여럿이면 <paramref name="prefer"/>와 이름이 같은 것을 우선, 없으면 'DH' 접두어를 우선.</summary>
    public static StyleInfo? PickByClass(Database db, CivilDocument doc, string cls, string? prefer = null)
    {
        var cands = Collect(db, doc, si => string.Equals(si.Cls, cls, StringComparison.OrdinalIgnoreCase));
        if (cands.Count == 0) return null;
        if (prefer != null)
        {
            var exact = cands.FirstOrDefault(s => Norm(s.Name) == Norm(prefer));
            if (!exact.Id.IsNull) return exact;
        }
        var dh = cands.FirstOrDefault(s => Norm(s.Name).StartsWith(Prefix, StringComparison.Ordinal));
        return dh.Id.IsNull ? cands[0] : dh;
    }

    /// <summary>현재 도면의 스타일 모음을 반사(reflection)로 훑는다 — 모음 이름이 Civil 3D 버전마다
    /// 조금씩 달라, 이름을 박아 두면 한 곳만 바뀌어도 전체가 멈춘다.</summary>
    private static void Walk(object? node, string path, int depth, Dictionary<string, StyleCollectionBase> outMap)
    {
        if (node == null || depth > 4) return;
        foreach (var p in node.GetType().GetProperties(BindingFlags.Public | BindingFlags.Instance))
        {
            if (p.GetIndexParameters().Length > 0 || !p.CanRead) continue;
            object? v;
            try { v = p.GetValue(node); } catch { continue; }
            if (v == null) continue;
            string key = path.Length == 0 ? p.Name : path + "." + p.Name;
            if (v is StyleCollectionBase col) { outMap[key] = col; continue; }
            var t = v.GetType();
            if (t.Namespace != null && t.Namespace.StartsWith("Autodesk.Civil", StringComparison.Ordinal))
                Walk(v, key, depth + 1, outMap);
        }
    }
}
