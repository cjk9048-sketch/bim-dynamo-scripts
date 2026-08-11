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

    /// <summary>★★[v27.0 · JACK 0811 실측] <b>종단 뷰의 '횡단 데이터' 서랍에 밴드 스타일을 직접 만든다.</b>
    ///
    /// <para><b>왜 필요한가.</b> JACK: <i>"DH_종단 뷰라고 되어 있는 건 종단 뷰의 횡단 데이터로 가야 해.
    /// 그런데 횡단 뷰의 밴드 스타일 ▸ 횡단면 데이터에 들어가 있어."</i> — 도구공간 실측 그대로였다.</para>
    ///
    /// <para><b>원인.</b> 두 서랍(<c>종단 뷰▸밴드▸횡단 데이터</c>와 <c>횡단 뷰▸밴드▸횡단면 데이터</c>)이
    /// <b>같은 내부 종류</b>(<c>AeccDbGraphStyleSectionalDataBands</c>)를 쓴다. <c>StyleBase.ExportTo</c>는
    /// <b>종류만 보고</b> 자리를 정하므로 이름이 '종단 뷰'여도 횡단 뷰 쪽에 넣는다.
    /// 종단도의 밴드는 <b>종단 뷰 서랍</b>에서 이름으로 찾으니 영영 못 찾는다 —
    /// <c>band style name is not found</c>도, 표가 비던 것도 전부 여기서 나왔다.</para>
    ///
    /// <para><b>실측으로 막힌 길(재시도 금지).</b>
    /// ① 밴드 세트를 먼저 보내 부품을 끌고 오게 → <b>안 끌고 온다</b>(서랍 0개).
    /// ② 밴드 스타일을 직접 <c>ExportTo</c> → <b>여전히 횡단 뷰 서랍</b>(서랍 0개).
    /// 남은 길은 하나다 — <b>맞는 서랍에 빈 스타일을 만들고 내용을 옮긴다.</b></para>
    ///
    /// <para>속을 옮길 때는 <b>이름을 하나하나 적지 않는다.</b> 스타일마다 속성이 수십 개인데
    /// 손으로 적으면 빠뜨린 것을 나중에 '왜 다르지'로 찾게 된다. Civil 스타일 값은
    /// <c>PropertyDouble</c>/<c>PropertyString</c>처럼 <b>전부 <c>.Value</c>를 가진 껍데기</b>라,
    /// 그 규칙 하나로 통째로 옮긴다.</para>
    /// 반환=사람이 읽을 결과 한 줄.</summary>
    public static string EnsureProfileSectionalBandStyles(Database db, CivilDocument cdoc)
    {
        try
        {
            var col = cdoc.Styles.BandStyles.ProfileViewSectionalDataBandStyles;

            // ── ① <b>맞는 서랍의 주소를 알아낸다.</b>
            //   서랍이 비어 있으면 주소를 알 길이 없으므로 <b>임시 스타일을 하나 만들었다 지운다</b>.
            //   (JACK 0811 제보: Civil 복제 우회의 정석 — 더미로 딕셔너리 ID를 얻는다.)
            ObjectId rightDict = ObjectId.Null;
            const string DummyName = "DH_임시_서랍찾기";
            using (var tr = db.TransactionManager.StartTransaction())
            {
                for (int i = 0; i < col.Count && rightDict.IsNull; i++)
                    try { rightDict = tr.GetObject(col[i], OpenMode.ForRead).OwnerId; } catch { }
                tr.Commit();
            }
            if (rightDict.IsNull)
            {
                try
                {
                    ObjectId dummy = col.Add(DummyName);
                    using var tr = db.TransactionManager.StartTransaction();
                    rightDict = tr.GetObject(dummy, OpenMode.ForRead).OwnerId;
                    try { tr.GetObject(dummy, OpenMode.ForWrite).Erase(); } catch { }
                    tr.Commit();
                }
                catch (Exception ex) { return "횡단 데이터 서랍 주소를 못 알아냈다 — " + ex.Message; }
            }

            // ── ② <b>납치된 스타일을 되찾아 온다.</b>
            //   JACK 0811 제보: 종단 뷰와 횡단 뷰의 횡단 데이터 밴드는 <b>같은 C# 클래스</b>를 쓰므로,
            //   복제할 때 객체가 <i>"나는 횡단 데이터 밴드니까 횡단 뷰로 가야지"</i> 하고
            //   <b>지정한 자리를 무시하고</b> 횡단 뷰 딕셔너리로 스스로 들어가 버린다.
            //   복사본을 새로 만들 필요가 없다 — <b>이미 도면에 있는 그 객체를 옮기면</b> 된다.
            //   (잘못된 딕셔너리에서 빼고 → 맞는 딕셔너리에 이름으로 넣는다.)
            var twins = Collect(db, cdoc, si => si.Cls.Contains("SectionalData", StringComparison.Ordinal)
                                             && Norm(si.Name).StartsWith(Prefix, StringComparison.Ordinal))
                        .GroupBy(s => Norm(s.Name)).Select(g => g.First()).ToList();

            // ★★[v29.0 점검 반영 · 치명] <b>빼기부터 하면 실패할 때 스타일이 증발한다.</b>
            //   종전 순서는 <c>헌 서랍에서 빼기 → 새 서랍에 넣기</c>였다. 그 사이에서 실패해도
            //   그대로 <c>Commit</c>했으므로 스타일이 <b>양쪽 어디에도 없는 상태로 확정 저장</b>됐다.
            //   → <b>넣기가 성공한 뒤에 뺀다.</b> 그리고 <b>한 건이라도 실패하면 통째로 되돌린다</b>
            //     (반쪽 상태로 저장하느니 아무것도 안 한 편이 낫다 — 다시 돌리면 되니까).
            int moved = 0, already = 0; var fail = new List<string>();
            using (var tr = db.TransactionManager.StartTransaction())
            {
                bool hurt = false;
                foreach (var s in twins)
                {
                    try
                    {
                        var o = tr.GetObject(s.Id, OpenMode.ForWrite);
                        if (o.OwnerId == rightDict) { already++; continue; }
                        ObjectId wrongId = o.OwnerId;
                        if (tr.GetObject(rightDict, OpenMode.ForWrite) is not DBDictionary right)
                        { fail.Add($"{s.Name}:종단뷰 사전을 못 엶"); continue; }
                        if (tr.GetObject(wrongId, OpenMode.ForWrite) is not DBDictionary wrong)
                        { fail.Add($"{s.Name}:원래자리가 사전이 아님"); continue; }

                        right.SetAt(s.Name, o);                     // ① 먼저 새 자리에 넣고
                        if (o.OwnerId != rightDict)                 //    되읽어 확인한 뒤에만
                        { fail.Add($"{s.Name}:새 자리에 안 앉음"); hurt = true; break; }
                        try { wrong.Remove(s.Id); }                 // ② 헌 자리에서 뺀다
                        catch (Exception ex) { fail.Add($"{s.Name}:헌자리 제거실패({ex.Message})"); }
                        moved++;
                    }
                    catch (Exception ex) { fail.Add($"{s.Name}:{ex.Message}"); hurt = true; break; }
                }
                if (hurt) { tr.Abort(); moved = 0; fail.Add("한 건이라도 다치면 통째로 되돌린다"); }
                else tr.Commit();
            }

            // ── ③ <b>옮겨졌는지 되읽어 확인한다</b>(넣었다고 세지 않는다).
            int after = 0; var names = new List<string>();
            try
            {
                using var tr = db.TransactionManager.StartTransaction();
                after = col.Count;
                for (int i = 0; i < col.Count; i++)
                    try { if (tr.GetObject(col[i], OpenMode.ForRead) is StyleBase sb) names.Add(sb.Name); } catch { }
                tr.Commit();
            }
            catch { }

            // ★★[v29.0 점검 반영 · 치명] <b>"하나라도 됐으면 성공"이 아니라 "전부 제자리인가"로 판정한다.</b>
            //   종전엔 열 개 중 하나만 옮겨져도 여기서 끝냈다 — 나머지 아홉을 새로 만들어 주는
            //   예비 경로가 <b>영영 실행되지 않았다</b>. 재실행해도 같은 판정이 나와 고착된다.
            //   판정은 <b>되읽은 서랍 목록</b>으로만 한다(넣었다고 세지 않는다).
            var have = new HashSet<string>(names.Select(Norm), StringComparer.Ordinal);
            var missing = twins.Where(s => !have.Contains(Norm(s.Name))).ToList();

            string res = $"횡단 데이터 밴드 스타일: 종단 뷰 서랍으로 {moved}개 옮김"
                       + (already > 0 ? $" · 이미 제자리 {already}개" : "")
                       + $" → 서랍 총 {after}개(그중 DH {have.Count(h => h.StartsWith(Prefix, StringComparison.Ordinal))}개)"
                       + (fail.Count > 0 ? " · 실패[" + string.Join(" | ", fail.Take(4)) + "]" : "")
                       + (names.Count > 0 ? "\n    들어 있는 것: " + string.Join(" · ", names) : "");
            if (missing.Count == 0) return res;
            res += $"\n    ⚠아직 제자리에 없는 것 {missing.Count}개 — 새로 만들어 속을 베낀다";

            // ── ④ 아직 제자리에 없는 것만 — 빈 스타일을 만들고 속을 베낀다(차선책).
            int made = 0;
            foreach (var s in missing)
            {
                ObjectId nid;
                try { nid = col.Add(s.Name); }
                catch (Exception ex) { fail.Add($"{s.Name}:만들기실패({ex.Message})"); continue; }
                try
                {
                    using var tr = db.TransactionManager.StartTransaction();
                    var src = tr.GetObject(s.Id, OpenMode.ForRead) as SectionalDataBandStyle;
                    var dst = tr.GetObject(nid, OpenMode.ForWrite) as SectionalDataBandStyle;
                    if (src == null || dst == null) { fail.Add($"{s.Name}:열기실패"); tr.Commit(); continue; }

                    // ⓐ 밴드 스타일 자체의 값 — <b>베낄 것을 이름으로 못박는다.</b>
                    //
                    //   ★★[v29.0 점검 반영 · 치명] 종전엔 "읽고 쓸 수 있는 것 전부"를 베꼈는데,
                    //   그 안에 <b>주인 표시(OwnerId)</b>가 섞여 있었다. 새로 만든 스타일에
                    //   <i>"네 주인은 아직 횡단 뷰 서랍이다"</i>라고 도로 써 넣은 셈이라,
                    //   <b>서랍을 바로잡으려고 만든 함수가 자기 결과를 원위치</b>시켰다.
                    //   게다가 예외를 삼켜서 로그에도 안 남았다.
                    foreach (string pn in new[] { "BandHeight", "TextHeight", "TextBoxWidth", "OffsetFromBand",
                                                  "TextLocation", "TextBoxPosition", "Text", "TextStyle", "WeedingFactor" })
                    {
                        try
                        {
                            var p = typeof(BandStyle).GetProperty(pn);
                            if (p == null || !p.CanRead || !p.CanWrite) { fail.Add($"{s.Name}:{pn}(못씀)"); continue; }
                            p.SetValue(dst, p.GetValue(src));
                        }
                        catch (Exception ex) { fail.Add($"{s.Name}:{pn}({ex.GetType().Name})"); }
                    }

                    // ⓑ 라벨 스타일 셋(단면검토선 라벨 · 증분 라벨 · 제목) — 글자 구성요소까지
                    var skipped = new List<string>();
                    CopyLabel(tr, src.SampleLineStationLabelStyleId, dst.SampleLineStationLabelStyleId, skipped);
                    CopyLabel(tr, src.IncrementalSectionDataLabelStyleId, dst.IncrementalSectionDataLabelStyleId, skipped);
                    CopyLabel(tr, src.TitleTextLabelStyleId, dst.TitleTextLabelStyleId, skipped);
                    if (skipped.Count > 0) fail.Add($"{s.Name}:못베낀 라벨값 {skipped.Count}개[{string.Join(",", skipped.Take(5))}]");

                    // ⓒ 눈금
                    foreach (var (a, b) in new[] { (src.SampleLineStationTickStyle, dst.SampleLineStationTickStyle),
                                                   (src.IncrementalSectionDataTickStyle, dst.IncrementalSectionDataTickStyle) })
                        try
                        {
                            foreach (var p in typeof(BandTickStyle).GetProperties())
                                if (p.CanRead && p.CanWrite) try { p.SetValue(b, p.GetValue(a)); } catch { }
                        }
                        catch { }

                    // ⓓ 표시(보임·색)
                    foreach (var v in Enum.GetValues(typeof(SectionalDataDisplayStyleType)))
                        try
                        {
                            var t = (SectionalDataDisplayStyleType)v;
                            using var ds = src.GetDisplayStylePlan(t);
                            using var dd = dst.GetDisplayStylePlan(t);
                            dd.Visible = ds.Visible;
                            try { dd.Color = ds.Color; } catch { }
                        }
                        catch { }

                    tr.Commit();
                    made++;
                }
                catch (Exception ex) { fail.Add($"{s.Name}:복사실패({ex.Message})"); }
            }

            int after2 = -1;
            try { after2 = col.Count; } catch { }
            return $"횡단 데이터 밴드 스타일: 옮기기 실패 → 새로 만들어 속을 베낌 {made}개(서랍 총 {after2}개)"
                 + (fail.Count > 0 ? " · 실패[" + string.Join(" | ", fail.Take(4)) + "]" : "");
        }
        catch (Exception ex) { return "횡단 데이터 밴드 스타일 만들기 실패 — " + ex.Message; }
    }

    /// <summary>라벨 스타일 하나를 통째로 베낀다 — 글자 구성요소가 모자라면 만들어서 채운다.
    /// <para>Civil 스타일 값은 <c>.Value</c>를 가진 껍데기라, 이름을 몰라도 그 규칙으로 옮길 수 있다.</para></summary>
    private static void CopyLabel(Transaction tr, ObjectId srcId, ObjectId dstId, List<string>? skipped = null)
    {
        if (srcId.IsNull || dstId.IsNull || srcId == dstId) return;
        try
        {
            if (tr.GetObject(srcId, OpenMode.ForRead) is not LabelStyle s) return;
            if (tr.GetObject(dstId, OpenMode.ForWrite) is not LabelStyle d) return;

            using (var sc0 = s.GetComponents(LabelStyleComponentType.Text))
            using (var dc0 = d.GetComponents(LabelStyleComponentType.Text))
                for (int i = dc0.Count; i < sc0.Count; i++)
                    try { d.AddComponent("Text" + (i + 1), LabelStyleComponentType.Text); } catch { }

            using var sc = s.GetComponents(LabelStyleComponentType.Text);
            using var dc = d.GetComponents(LabelStyleComponentType.Text);
            for (int i = 0; i < sc.Count && i < dc.Count; i++)
            {
                try
                {
                    if (tr.GetObject(sc[i], OpenMode.ForRead) is not LabelStyleTextComponent a) continue;
                    if (tr.GetObject(dc[i], OpenMode.ForWrite) is not LabelStyleTextComponent b) continue;
                    CopyValues(a.Text, b.Text, skipped, "Text");
                    CopyValues(a.General, b.General, skipped, "General");
                }
                catch { }
            }
        }
        catch { }
    }

    /// <summary><c>.Value</c>를 가진 속성을 통째로 베낀다(읽기 전용·다른 종류는 건너뛴다).
    /// <para>★★[v29.0 점검 반영] <b>건너뛴 것을 센다.</b> 종전엔 조용히 넘어가서, 글자 정렬·앵커가
    /// 안 베껴졌는데도 "다 됐다"로 보였다 — 그게 <b>글자가 칸에서 삐뚤어지던</b> 원인 중 하나다.
    /// 값을 읽을 때는 <b>실제 타입 기준</b>으로 <c>Value</c>를 찾는다(선언 타입으로 찾으면
    /// 파생 타입에서만 보이는 것들을 놓친다).</para></summary>
    private static void CopyValues(object? src, object? dst, List<string>? skipped = null, string tag = "")
    {
        if (src == null || dst == null) return;
        foreach (var p in src.GetType().GetProperties())
        {
            try
            {
                if (!p.CanRead) continue;
                object? sv = p.GetValue(src), dv = p.GetValue(dst);
                if (sv == null || dv == null) { skipped?.Add($"{tag}.{p.Name}(null)"); continue; }
                var vp = sv.GetType().GetProperty("Value");   // 선언 타입이 아니라 실제 타입에서 찾는다
                if (vp == null) continue;                     // 값 껍데기가 아닌 속성 — 대상 아님
                if (!vp.CanRead || !vp.CanWrite) { skipped?.Add($"{tag}.{p.Name}(읽기전용)"); continue; }
                vp.SetValue(dv, vp.GetValue(sv));
            }
            catch (Exception ex) { skipped?.Add($"{tag}.{p.Name}({ex.GetType().Name})"); }
        }
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
            void Ship(List<StyleInfo> list)
            {
                if (list.Count == 0) return;
                try
                {
                    var ids = new ObjectIdCollection();
                    foreach (var s in list) ids.Add(s.Id);
                    StyleBase.ExportTo(ids, dstDb, Autodesk.Civil.StyleConflictResolverType.Ignore);
                }
                catch (Exception exAll)
                {
                    fail.Add("일괄실패:" + exAll.Message);
                    foreach (var s in list)
                    {
                        try
                        {
                            StyleBase.ExportTo(new ObjectIdCollection { s.Id }, dstDb,
                                               Autodesk.Civil.StyleConflictResolverType.Ignore);
                        }
                        catch (Exception ex) { fail.Add($"{s.Name}:{ex.Message}"); }
                    }
                }
            }

            /// 종단 뷰 ▸ 밴드 ▸ 횡단 데이터 서랍에 이 이름이 앉아 있는가 — <b>Civil의 눈으로</b> 센다.
            int DrawerCount()
            {
                try
                {
                    var col = dstCivil.Styles.BandStyles.ProfileViewSectionalDataBandStyles;
                    int c = 0;
                    using var tr2 = dstDb.TransactionManager.StartTransaction();
                    for (int i = 0; i < col.Count; i++)
                        try
                        {
                            if (tr2.GetObject(col[i], OpenMode.ForRead) is StyleBase sb2 &&
                                Norm(sb2.Name).StartsWith(Prefix, StringComparison.Ordinal)) c++;
                        }
                        catch { }
                    tr2.Commit();
                    return c;
                }
                catch { return -1; }
            }

            // ★★[v26.2 · JACK 0811 실측] <b>스타일이 엉뚱한 서랍에 들어갔다.</b>
            //
            //   도구공간 실측: <c>DH_종단 뷰_횡단 데이터_*</c> 10개가 전부
            //   <b>횡단 뷰 ▸ 밴드 스타일 ▸ 횡단면 데이터</b>에 앉아 있었다. 이름은 '종단 뷰'인데 자리가 다르다.
            //   두 서랍이 <b>같은 내부 종류</b>(<c>AeccDbGraphStyleSectionalDataBands</c>)를 쓰기 때문에
            //   <c>StyleBase.ExportTo</c>가 종류만 보고 횡단 뷰 쪽에 넣어버린 것이다.
            //   종단도의 밴드는 <b>종단 뷰 서랍</b>에서 찾으므로 영영 못 찾는다 —
            //   <c>band style name is not found</c>도, 값이 안 그려지던 것도 전부 이 하나에서 나왔다.
            //
            //   → <b>순서를 바꾼다.</b> 밴드 스타일을 먼저 던지지 말고 <b>나머지를 먼저</b> 보낸다.
            //     거기에 <b>정보표시 테이블(밴드 세트)</b>이 들어 있고, 세트를 복사하면 Civil이
            //     <b>자기가 쓰는 밴드 스타일을 제자리에 끌고 온다</b>. 그러고도 비어 있으면
            //     그때 직접 보낸다(엉뚱한 자리라도 없는 것보다는 낫고, 로그에 그대로 남는다).
            var sectStyles = todo.Where(s => s.Cls.Contains("SectionalData", StringComparison.Ordinal)).ToList();
            var others = todo.Where(s => !s.Cls.Contains("SectionalData", StringComparison.Ordinal)).ToList();
            Ship(others);
            int drawer0 = DrawerCount();
            if (drawer0 <= 0)
            {
                fail.Add($"세트가 횡단 데이터 밴드를 안 끌고 옴(서랍 {drawer0}개) — 직접 보냄");
                Ship(sectStyles);
            }
            int drawer1 = DrawerCount();

            // ★★[v25.9 · JACK 0811] <b>"정보표시 테이블은 가져온 것 같은데 그 꾸러미에 든 스타일 자체는
            //   못 가져온 거 같은데?"</b> — <b>맞았다.</b> 도구공간 트리 실측:
            //   <c>밴드 스타일 ▸ 정보표시 테이블</c>에는 <c>DH_..._토공/도로/관로</c>가 들어와 있는데,
            //   <c>밴드 스타일 ▸ 횡단 데이터</c>에는 순정 <c>Sample Line Name and Distance</c> 하나뿐이었다.
            //   <b>세트만 들어오고 그 안의 밴드 스타일은 안 들어왔다.</b>
            //   그러면 밴드가 <b>도면에 없는 스타일</b>을 가리키게 되어 그려질 수가 없다
            //   (<c>Add(종류, 이름)</c>이 "band style name is not found"로 실패한 것도 같은 이유다).
            //
            //   <b>일괄 복사가 예외 없이 조용히 빠뜨렸다.</b> 그래서 종전의 '예외가 나면 하나씩' 안전판이
            //   작동할 기회조차 없었다 — <b>예외가 아니라 결과로 판정</b>해야 한다.
            //   이 저장소의 규율 그대로: <b>넣었다고 세지 말고 되읽어 확인한다.</b>
            string verify;
            int retried = 0, stillMissing = 0;
            try
            {
                var here2 = new HashSet<string>(
                    Collect(dstDb, dstCivil, null).Select(s => Norm(s.Name)), StringComparer.Ordinal);
                var missing = todo.Where(s => !here2.Contains(Norm(s.Name))).ToList();
                foreach (var s in missing)
                {
                    try
                    {
                        StyleBase.ExportTo(new ObjectIdCollection { s.Id }, dstDb,
                                           Autodesk.Civil.StyleConflictResolverType.Ignore);
                        retried++;
                    }
                    catch (Exception ex) { fail.Add($"{s.Name}(재시도):{ex.Message}"); }
                }

                var after = Collect(dstDb, dstCivil, si => Norm(si.Name).StartsWith(Prefix, StringComparison.Ordinal));
                var hereAfter = new HashSet<string>(after.Select(s => Norm(s.Name)), StringComparer.Ordinal);
                var gone = todo.Where(s => !hereAfter.Contains(Norm(s.Name))).ToList();
                stillMissing = gone.Count;
                okCount = todo.Count - stillMissing;

                // ★★[v26.1 · JACK 0811] <b>"여전히 복사가 안 됐어"</b> — 도구공간에는
                //   <c>Sample Line Name and Distance</c> 하나뿐인데 코드는 10개가 있다고 셌다.
                //   둘 중 하나가 거짓말인데, <b>어느 서랍에 들어갔는지</b>를 안 찍어서 갈리지 않았다.
                //   Civil은 이름이 같아도 <b>다른 컬렉션</b>에 앉으면 그 자리에서 못 찾는다 —
                //   밴드가 스타일을 못 찾는 증상(<c>band style name is not found</c>)과 정확히 맞는다.
                //   → <b>자리(Path)까지</b> 남기고, 종단 뷰의 횡단 데이터 서랍을 <b>Civil의 눈으로</b> 따로 센다.
                var sect = after.Where(s => s.Cls.Contains("SectionalData", StringComparison.Ordinal)).ToList();
                string drawer = $"\n    ▶종단 뷰▸밴드▸횡단 데이터 서랍: 세트 복사 후 {drawer0}개 → 최종 {drawer1}개"
                              + (drawer1 <= 0 ? "  ⚠비었다 — 밴드가 스타일을 못 찾는다" : "");
                try
                {
                    var names = new List<string>();
                    var col = dstCivil.Styles.BandStyles.ProfileViewSectionalDataBandStyles;
                    using (var tr2 = dstDb.TransactionManager.StartTransaction())
                    {
                        for (int i = 0; i < col.Count; i++)
                            try { if (tr2.GetObject(col[i], OpenMode.ForRead) is StyleBase sb2) names.Add(sb2.Name); } catch { }
                        tr2.Commit();
                    }
                    drawer += "\n      들어 있는 것: " + string.Join(" · ", names);
                }
                catch (Exception ex) { drawer += "\n      서랍을 못 열었다 — " + ex.Message; }

                verify = $"  들여온 뒤 도면 실측: DH 스타일 {after.Count}개"
                       + (missing.Count > 0 ? $" · 일괄이 빠뜨린 {missing.Count}개를 하나씩 재시도({retried}개 성공)" : "")
                       + $"\n    이름이 '횡단 데이터'인 밴드 스타일 {sect.Count}개"
                       + (sect.Count > 0 ? ":\n      " + string.Join("\n      ", sect.Select(s => $"{s.Name}  @{s.Path}"))
                                         : "  ⚠하나도 없다 — 밴드가 그려질 수 없다")
                       + drawer
                       + (stillMissing > 0 ? $"\n    ⚠끝내 안 들어온 것 {stillMissing}개: "
                                             + string.Join(" · ", gone.Take(10).Select(s => $"{s.Name}[{s.Cls}]")) : "");
            }
            catch (Exception ex) { verify = "  들여온 뒤 실측 실패 — " + ex.Message; }

            LastProbe = "  가져온 스타일(이름 · 종류 · 자리):\n    "
                      + string.Join("\n    ", todo.Select(s => $"{s.Name}  [{s.Cls}]  @{s.Path}"))
                      + "\n" + verify;
            LastReport = $"스타일: 들여옴 {okCount}/{todo.Count}개(되읽어 확인)"
                       + (retried > 0 ? $" · 일괄 누락 재시도 {retried}개" : "")
                       + (had > 0 ? $" · 이미있음 {had}개" : "")
                       + (stillMissing > 0 ? $" · ⚠끝내 누락 {stillMissing}개(로그 확인)" : "")
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

    /// <summary>★[JACK 0810] 정보표시 테이블(밴드 세트) 스타일을 <b>이름 조각으로</b> 고른다 —
    /// '관로'·'토공'·'도로'. 전체 이름을 박아 두면 템플릿에서 이름을 조금만 고쳐도 못 찾는다.</summary>
    public static StyleInfo? PickBandSet(Database db, CivilDocument doc, string keyword)
    {
        var cands = Collect(db, doc, si => si.Cls.Contains("ProfileBandStyleSet"));
        if (cands.Count == 0) return null;
        var hit = cands.FirstOrDefault(s => s.Name.Contains(keyword, StringComparison.Ordinal));
        return hit.Id.IsNull ? null : hit;
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
