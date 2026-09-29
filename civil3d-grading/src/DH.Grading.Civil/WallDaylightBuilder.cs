using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.Civil.DatabaseServices;
using DH.Grading.Core;

namespace DH.Grading.Civil;

/// <summary>★★★[JACK 0918] <b>가상옹벽_DH의 데이라잇</b>을 닫힌 3D 폴리선으로 그린다 — 상대 둘.
///
/// <para>①<b>원지반</b>(빨강 <c>DH-옹벽데이라잇</c>) ②<b>계획지표면 = 정지면_DH</b>(파랑 <c>DH-옹벽데이라잇-계획</c>).
/// JACK: <i>"이제 계획지표면하고 가상옹벽의 데이라잇도 구하고싶어"</i> — 인터뷰로 정한 것: 계획지표면은 정지면_DH,
/// 원지반 때와 <b>같은 규칙</b>(닫힌 고리 하나 · 가장 큰 것 · 끊기면 테두리로 · 1cm 정리 · 자동).</para>
///
/// <para>계산은 전부 <see cref="WallDaylight.Build"/>(Core — 하네스 S138이 잰다)가 한다.
/// 여기는 ①삼각형을 꺼내고 ②입력을 파일로 떨구고(하네스가 <b>같은 입력</b>으로 다시 돌린다)
/// ③그리는 일만 한다.</para>
///
/// <para>★<b>어디에 그리나.</b> 선이 <b>틀린 줄 아는</b> 경우(<see cref="WallDaylight.Result.Broken"/>)는
/// 정식 레이어에 <b>안 그린다</b> — 다음 단계(옹벽을 데이라잇으로 자르기)가 그 선을 칼로 쓰기 때문이다.
/// 그때는 확인용 레이어(노랑)에 그리고 명령줄에 ⚠를 띄운다.</para></summary>
public static class WallDaylightBuilder
{
    /// <summary>상대면 하나의 그리기 규칙.</summary>
    public sealed record Kind(string Tag, string Layer, short Aci, string LayerSuspect, string DumpName);

    public static readonly Kind Ground = new("원지반", "DH-옹벽데이라잇", 1, "DH-옹벽데이라잇-확인", "DHWALLDL_입력.txt");
    /// <summary>★[JACK 0918 인터뷰] 계획지표면 = 정지면_DH · 파랑(5).</summary>
    public static readonly Kind Plan = new("계획지표면", "DH-옹벽데이라잇-계획", 5, "DH-옹벽데이라잇-계획-확인", "DHWALLDL_입력_계획.txt");

    /// <summary>계획지표면으로 쓰는 지표면의 기준 이름.</summary>
    public const string PlanSurfaceBase = "정지면_DH";

    /// <summary>★[JACK 0918 인터뷰] 남길 옹벽 띠 — 초록(3) · 틀린 줄 아는 것은 노랑 확인용.</summary>
    public const string BandLayer = "DH-옹벽남길띠", BandLayerSuspect = "DH-옹벽남길띠-확인";
    public const string BandDump = "DHWALLDL_입력_띠.txt";
    /// <summary>★[JACK 0918 인터뷰] 띠를 경계 하나로 넣은 새 지표면 — 가상옹벽_DH는 재료로 두고 숨긴다.</summary>
    public const string PureName = "순수옹벽_DH";

    /// <summary>명령줄에 찍을 줄들 — 옹벽변환 분기 끝에서 쓴다. 분기 시작(<see cref="EraseOld"/>)에서 비운다.</summary>
    public static readonly System.Collections.Generic.List<string> Summaries = new();

    /// <summary>★[검토 0918 · 중간 6] <b>지난 실행의 선을 지운다</b> — 옹벽변환 분기에 들어서자마자 부른다.
    /// 손 폴리곤이 거절되거나 옹벽을 못 세우면 이 파일까지 안 오는데, 그때 지난 선이 <b>새것처럼</b> 남으면 안 된다.</summary>
    /// <param name="pureToo">지난 <c>순수옹벽_DH</c>도 지운다 — 폴리곤만 만드는 분기(여기서 새로 짓는다)에서만 참.
    /// 다른 길(전이면 단계)은 그 이름을 제 방식으로 다루므로 건드리지 않는다.</param>
    public static void EraseOld(Database db, Transaction tr, bool pureToo = false, ObjectId protect = default)
    {
        Summaries.Clear();
        EraseNote = "";
        // ★[v101.0 · 검토 0929 v101 · 중간 5] 이번 판이 데이라잇까지 가야 옹벽 합성의 짝이 다시 선다 — 중간에 멈추면 옛 짝을 안 쓴다
        GradingSettings.ClearLastWall(true);
        // ★[검토 v100.0 · 낮음 2] 원지반은 지키고(BuildVirtualSlope와 같은 규약) 지난 순수옹벽_DH만 지운다
        if (pureToo) { try { GradingBuilder.EraseSurfacesByBaseName(tr, PureName, protect); } catch { } }
        var (gone, stuck) = EraseLayers(db, tr, Ground.Layer, Ground.LayerSuspect, Plan.Layer, Plan.LayerSuspect,
                                        BandLayer, BandLayerSuspect);
        if (stuck > 0)
            EraseNote = $"⚠지난 데이라잇을 <b>{stuck}개 못 지웠다</b>(레이어가 잠겼는지 보세요) — 옛 선과 새 선이 같이 있을 수 있다";
        EraseCount = gone;
        // ★★[v101.1 · JACK 0929 «합성을 자동으로 풀기»] 옹벽을 다시 고치면 지난 옹벽 합성을 푼다 — 정지면_DH를 합성 전으로 되돌린다
        //   (옹벽 변환은 합성 전 계획면 기준이어야 한다 — 합성된 면이면 남길 띠가 사라진다). 계획부지 생성 «마지막 구역 다시»가 먼저 풀므로
        //   여기는 안전망이다(합성 전 면이 없으면 아무것도 안 한다). 원지반 보호 — 넘겨받은 것이 없으면 옹벽변환이 쓴 원지반
        try
        {
            ObjectId gProt = protect;
            if (gProt.IsNull) gProt = Commands.NoriCommand.FindByHandle(db, GradingSettings.LastGroundHandle);
            // 여기까지 오면 계획부지 생성 들머리가 이미 풀었거나(마지막 구역 다시) 합성전을 지웠다(이어서·새로) — 보통 할 일이 없고
            //   «합성 전 면 없는 합성» 안내만 붙는다. 못 풀었으면 들머리가 되돌리고 멈췄으므로 여기 안 온다
            var (un, _) = Commands.WallCompositeCommand.UndoComposite(db, tr, gProt);
            if (un.Length > 0) EraseNote = (EraseNote.Length > 0 ? EraseNote + " · " : "") + un;
        }
        catch { }
    }

    /// <summary>레이어들 위의 선을 지운다 — 모형공간을 <b>한 번만</b> 훑는다(검토 0918 v99.9 · 낮음).
    /// 훑는 도중에 지우지 않는다 — 먼저 모으고(GradingBuilder.EraseOnLayer와 같은 순서) 하나씩 지운다.</summary>
    private static (int Gone, int Stuck) EraseLayers(Database db, Transaction tr, params string[] layers)
    {
        int gone = 0, stuck = 0;
        try
        {
            var set = new System.Collections.Generic.HashSet<string>(layers, System.StringComparer.OrdinalIgnoreCase);
            var bt = (BlockTable)tr.GetObject(db.BlockTableId, OpenMode.ForRead);
            var ms = (BlockTableRecord)tr.GetObject(bt[BlockTableRecord.ModelSpace], OpenMode.ForRead);
            var ids = new System.Collections.Generic.List<ObjectId>();
            foreach (ObjectId id in ms)
                if (tr.GetObject(id, OpenMode.ForRead) is Autodesk.AutoCAD.DatabaseServices.Entity e && set.Contains(e.Layer)) ids.Add(id);
            foreach (var id in ids)
            {
                try { tr.GetObject(id, OpenMode.ForWrite).Erase(); gone++; }
                catch { stuck++; }        // 잠긴 레이어 등 — <b>세서</b> 알린다(검토 0918 · 중간 2)
            }
        }
        catch { stuck++; }
        return (gone, stuck);
    }

    /// <summary>직전 <see cref="EraseOld"/>가 지운 수 / 못 지운 사연.</summary>
    public static int EraseCount;
    public static string EraseNote = "";

    /// <summary>★둘 다 만든다 — 옹벽 삼각형은 <b>한 번만</b> 꺼낸다.</summary>
    /// <param name="bladeOk">옹벽 면이 성한가 — 브레이크라인을 Civil이 다 받았는가 등. 거짓이면
    /// 선이 맞아도 <b>확인용 레이어</b>로 보낸다(면이 틀렸으면 그 위에서 딴 선도 틀렸다).</param>
    /// <param name="bladeNote">그 까닭(로그·명령줄에 붙인다).</param>
    /// <returns>진단 로그에 붙일 추적표(여러 줄).</returns>
    /// <param name="rowsC">가상옹벽_DH를 지은 <b>닫은 줄</b> — 순수옹벽_DH를 같은 줄로 짓는다. null이면 띠만 그린다.</param>
    /// <param name="wUp">절토인가. 성토는 이번 단계에서 띠를 안 만든다(JACK: 한 단계씩).</param>
    public static string Build(Database db, Transaction tr, ObjectId wallId, IGroundSurface ground,
        ObjectId groundId, System.Collections.Generic.List<Point3> poly, bool bladeOk = true, string bladeNote = "",
        System.Collections.Generic.List<System.Collections.Generic.List<Point3>>? rowsC = null, bool wUp = true)
    {
        System.Collections.Generic.List<WallDaylight.Tri>? groundTris = null, planTris = null;
        string gName = "원지반", pName = PlanSurfaceBase;
        bool planGate = true; string planGateNote = "";
        var sb = new System.Text.StringBuilder();
        System.Collections.Generic.List<WallDaylight.Tri> wallTris;
        double mnx = double.MaxValue, mny = double.MaxValue, mxx = double.MinValue, mxy = double.MinValue;
        try
        {
            // 옹벽은 경계 뒤 <b>보이는 것</b>만
            var wallTin = (TinSurface)tr.GetObject(wallId, OpenMode.ForRead);
            var wallCache = new CachedGroundSurface(wallTin);
            var wb = wallCache.XYBounds;
            wallTris = wallCache.TrianglesIn(wb.MinX, wb.MinY, wb.MaxX, wb.MaxY);
            foreach (var q in poly)
            { mnx = Math.Min(mnx, q.X); mny = Math.Min(mny, q.Y); mxx = Math.Max(mxx, q.X); mxy = Math.Max(mxy, q.Y); }
            // ★[v101.0 · 검토 0929 v101 · 중간 5] 옹벽 합성이 쓸 짝 — 이 폴리곤(점 그대로) · 이 가상옹벽 · 아래 PlanPart가 고른 정지면
            GradingSettings.ClearLastWall(false);
            GradingSettings.LastWallPoly = new System.Collections.Generic.List<Point3>(poly);
            GradingSettings.LastWallSurfHandle = wallId.Handle.ToString();
            GradingSettings.LastWallPlanNote = "정지면_DH를 못 골랐다";
            GradingSettings.LastWallStamp = System.DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");
        }
        catch (System.Exception ex)
        {
            string m = $"⚠데이라잇 못 만듦 — 가상옹벽 삼각형을 못 꺼냈다 {ex.GetType().Name}: {ex.Message}";
            Summaries.Add(m);
            return "  " + m + "\n";
        }

        // ① 원지반 — 이미 캐시된 것에서 폴리곤 상자 + 1m
        {
            var gCache = ground as CachedGroundSurface;
            try { if (tr.GetObject(groundId, OpenMode.ForRead) is TinSurface gt) { gName = gt.Name; gCache ??= new CachedGroundSurface(gt); } }
            catch { }
            if (gCache == null)
            {
                string m = "⚠원지반 데이라잇 못 만듦 — 원지반 삼각형을 못 꺼냈다";
                Summaries.Add(m); sb.Append("  " + m + "\n");
            }
            else
            {
                groundTris = gCache.TrianglesIn(mnx - 1.0, mny - 1.0, mxx + 1.0, mxy + 1.0);
                sb.Append(RunOne(db, tr, Ground, poly, wallTris, groundTris, gName, bladeOk, bladeNote, ""));
            }
        }

        // ② 계획지표면(정지면_DH) — 옹벽 변환은 이 면을 <b>안 건드리므로</b> 지난 «계획부지 생성»의 결과다
        string? planMiss = PlanPart();
        if (planMiss != null) { Summaries.Add(planMiss); sb.Append("  " + planMiss + "\n"); GradingSettings.LastWallPlanNote = planMiss; }
        // ★[3차 검토 0929 · 낮음 1] 성토 구간은 순수옹벽을 안 짓는다(절토만 — 성토를 아래로 고치는 것이 다음 단계) — 합성도 짓지 않게 짝에 까닭을 남긴다
        if (!wUp) GradingSettings.LastWallPlanNote = "성토 옹벽은 아직 합성하지 않는다(이번 단계는 절토만 — 성토 옹벽을 아래로 고치는 것이 다음 단계)";

        // ③ ★★★[JACK 0918] 남길 옹벽 띠 → 순수옹벽_DH
        if (!wUp)
            Summaries.Add("[남길 띠] 성토 구간 — 이번 단계는 절토만 만든다(성토 옹벽을 아래로 고치는 것이 다음 단계)");
        else if (groundTris == null || planTris == null)
            Summaries.Add("[남길 띠] ⚠못 만듦 — " + (groundTris == null ? "원지반" : "계획지표면") + " 삼각형이 없다");
        else
            sb.Append(RunBand(db, tr, poly, wallTris, groundTris, gName, planTris, pName, rowsC, groundId, wallId,
                              bladeOk && planGate, !bladeOk ? bladeNote : planGateNote));
        return sb.ToString();

        // 계획지표면 부분 — 못 하면 사유 문자열, 하면 null
        string? PlanPart()
        {
            var sw = System.Diagnostics.Stopwatch.StartNew();
            string note = "";
            CachedGroundSurface? pCache = null;
            int hits1 = 0;
            bool stale = false;
            bool composed = false;   // ★[v101.1 · 계획 검토 높음 1] 정지면_DH가 옹벽 합성(합성 전 면 없이 남은 것)
            try
            {
                // 같은 기준 이름이 여럿이면(정지면_DH · 정지면_DH_2 …) <b>무엇을 썼는지</b> 밝힌다 — 정확한 이름이 먼저
                // ★★[검토 0918 v99.9 · 중간 2] <b>원지반 자신은 후보에서 뺀다</b> — 원지반 이름이 정지면_DH인 도면이 있다
                //   (그러면 파란 선이 빨간 선과 똑같아진다). 여럿이면 <b>가장 큰 번호</b>(=가장 나중에 지은 것)를 쓴다 —
                //   합성은 옛 면을 못 지우면 새 면을 «_2»로 짓는다. 그래도 어느 것이 맞는지 확신할 수 없으므로
                //   여럿일 때는 <b>확인용 레이어</b>로 보낸다.
                var civilDoc = Autodesk.Civil.ApplicationServices.CivilApplication.ActiveDocument;
                var hits = new System.Collections.Generic.List<(ObjectId Id, string Name, int N)>();
                foreach (ObjectId sid in civilDoc.GetSurfaceIds())
                {
                    if (sid == groundId) continue;
                    if (tr.GetObject(sid, OpenMode.ForRead) is not Autodesk.Civil.DatabaseServices.Surface s) continue;
                    string nm = s.Name;
                    if (nm == PlanSurfaceBase) hits.Add((sid, nm, 0));
                    else if (nm.StartsWith(PlanSurfaceBase + "_") && int.TryParse(nm.Substring(PlanSurfaceBase.Length + 1), out int nn))
                        hits.Add((sid, nm, nn));
                }
                hits.Sort((a, b) => b.N.CompareTo(a.N));
                if (hits.Count == 0)
                    return $"⚠계획지표면 데이라잇 없음 — '{PlanSurfaceBase}'가 없다(계획부지 생성을 먼저 돌리세요)";
                if (hits.Count > 1)
                    note = $"'{PlanSurfaceBase}' 이름의 지표면이 {hits.Count}개({string.Join(", ", hits.ConvertAll(h => h.Name))}) — 가장 나중 것 '{hits[0].Name}'를 썼다(어느 것이 맞는지 확인)";
                pName = hits[0].Name;
                hits1 = hits.Count;
                GradingSettings.LastWallPlanHandle = hits[0].Id.Handle.ToString();
                if (tr.GetObject(hits[0].Id, OpenMode.ForRead) is not TinSurface pt)
                    return $"⚠계획지표면 데이라잇 못 만듦 — '{pName}'가 TIN 지표면이 아니다";
                // ★[검토 0918 v99.9 · 중간 3] 낡은 면이면 적는다(옹벽 변환은 정지면을 다시 짓지 않는다)
                try { if (pt.IsOutOfDate) { stale = true; note += (note.Length > 0 ? " · " : "") + $"'{pName}'가 <b>낡음(Out of date)</b> 상태"; } } catch { }
                // ★[v101.1 · 계획 검토 높음 1·중간 2] 합성 전 면 없이 남은 옹벽 합성이면 그 위에서 딴 띠·파랑 선은 틀린다(옛 옹벽 머리가 섞인다)
                try
                {
                    if (Commands.WallCompositeCommand.IsComposite(tr, pt))
                    { composed = true; note += (note.Length > 0 ? " · " : "") + $"'{pName}'는 <b>옹벽 합성</b>인데 합성 전 면이 없어 못 풀었다 — {Commands.WallCompositeCommand.B1Help}"; }
                }
                catch { }
                pCache = new CachedGroundSurface(pt);
            }
            catch (System.Exception ex)
            { return $"⚠계획지표면 데이라잇 못 만듦 — '{pName}'를 못 읽었다 {ex.GetType().Name}: {ex.Message}"; }
            long tRead = sw.ElapsedMilliseconds;
            // 이름이 여럿이면 어느 면이 맞는지 모른다 — 선은 맞아도 확인용으로
            bool planOk = bladeOk && hits1 <= 1 && !composed;
            string planNote = !bladeOk ? bladeNote : hits1 > 1 ? "계획지표면 후보가 여럿" : composed ? $"'{pName}'가 옹벽 합성(합성 전 면 없음)" : "";
            // ★[검토 v100.0 · 낮음 3] 여기서 터지면 바깥이 «옹벽을 못 세웠다»로 잘못 적는다 — 제 자리에서 잡는다
            try { planTris = pCache.TrianglesIn(mnx - 1.0, mny - 1.0, mxx + 1.0, mxy + 1.0); }
            catch (System.Exception tx) { planTris = null; return $"⚠계획지표면 데이라잇 못 만듦 — '{pName}' 삼각형을 못 꺼냈다 {tx.GetType().Name}"; }
            sb.Append(RunOne(db, tr, Plan, poly, wallTris, planTris, pName, planOk, planNote,
                $"'{pName}' 삼각형 읽기 {tRead}ms" + (note.Length > 0 ? " · ⚠" + note : "")));
            if (note.Length > 0 && Summaries.Count > 0) Summaries[^1] += " · ⚠" + note;
            // ★[계획 검토 0918 · 중간 2] 순수옹벽_DH 관문 — 후보 여럿 · 낡은 정지면이면 <b>면을 짓지 않는다</b>
            planGate = hits1 <= 1 && !stale && !composed;
            planGateNote = hits1 > 1 ? "계획지표면 후보가 여럿" : stale ? $"'{pName}'가 낡음(Out of date)"
                         : composed ? $"'{pName}'가 옹벽 합성인데 합성 전 면이 없다 — {Commands.WallCompositeCommand.B1Help}" : "";
            GradingSettings.LastWallPlanNote = !bladeOk ? bladeNote : planGateNote;      // 순수옹벽_DH 관문과 같은 까닭이면 합성도 안 짓는다
            return null;
        }
    }

    /// <summary>★★★[JACK 0918] 남길 옹벽 띠를 그리고 <b>순수옹벽_DH</b>를 짓는다 — Outer 하나(+안쪽 구멍만 Hide).</summary>
    private static string RunBand(Database db, Transaction tr, System.Collections.Generic.List<Point3> poly,
        System.Collections.Generic.List<WallDaylight.Tri> wallTris,
        System.Collections.Generic.List<WallDaylight.Tri> groundTris, string gName,
        System.Collections.Generic.List<WallDaylight.Tri> planTris, string pName,
        System.Collections.Generic.List<System.Collections.Generic.List<Point3>>? rowsC, ObjectId groundId, ObjectId wallId,
        bool gateOk, string gateNote)
    {
        var sb = new System.Text.StringBuilder();
        const string head = "[남길 띠] ";
        try
        {
            // 입력을 <b>한 파일</b>에(세 삼각망) — 두 파일을 짝지으면 어긋날 수 있다(계획 검토 0918)
            string dump;
            try
            {
                dump = System.IO.Path.Combine(System.IO.Path.GetDirectoryName(DiagLog.FilePath) ?? ".", BandDump);
                WallDaylight.WriteBandInput(dump, poly, wallTris, groundTris, planTris,
                    $"{System.DateTime.Now:yyyy-MM-dd HH:mm:ss} {GradingSettings.Version} 원지반='{gName}' 계획='{pName}'");
                ArchiveDump(dump);
            }
            catch (System.Exception de) { dump = $"(못 떨굼: {de.GetType().Name})"; }

            var r = WallDaylight.KeepBand(poly, wallTris, groundTris, planTris, gName, pName);
            sb.Append("  ── [남길 띠] ──\n").Append(r.Trace).Append($"    입력 파일: {dump}\n");
            if (r.Ring == null) { Summaries.Add(head + r.Summary); return sb.ToString(); }

            bool broken = r.Broken || !gateOk || rowsC == null;
            string why = r.Broken ? "계산에 틀린 곳이 있다(로그 ⑨)" : !gateOk ? gateNote : rowsC == null ? "옹벽 줄이 없다" : "";
            string layer = broken ? BandLayerSuspect : BandLayer;
            short aci = broken ? (short)2 : (short)3;
            ObjectId ringId;
            try
            {
                ringId = DrawClosed(db, tr, r.Ring, layer, aci);
                foreach (var h in r.Holes) DrawClosed(db, tr, h, layer, aci);
            }
            catch (System.Exception dx)
            {
                Summaries.Add(head + $"⚠띠를 계산했지만 <b>못 그렸다</b> — {dx.GetType().Name}: {dx.Message}");
                return sb.ToString();
            }
            string rb;
            {
                var pl = (Polyline3d)tr.GetObject(ringId, OpenMode.ForRead);
                int nv = 0; foreach (ObjectId vid in pl) nv++;
                rb = $"되읽기: 닫힘 {(pl.Closed ? "예" : "<b>아니오</b>")} · 정점 {nv}개(기대 {r.Ring.Count}) · 레이어 '{pl.Layer}'";
                if (!pl.Closed || nv != r.Ring.Count) { rb = "⚠" + rb; if (!broken) { broken = true; why = "띠 선 되읽기가 어긋났다"; pl.UpgradeOpen(); pl.LayerId = GradingBuilder.EnsureLayer(db, tr, BandLayerSuspect, 2); } }
            }
            sb.Append($"    {rb}\n");
            if (broken)
            {
                Summaries.Add(head + $"⚠확인용 레이어 '{BandLayerSuspect}'(노랑)에만 그렸다 — <b>순수옹벽_DH는 안 만들었다</b>({why}) · " + r.Summary);
                return sb.ToString();
            }

            // ── 순수옹벽_DH — 같은 줄 · Outer 하나 · 구멍만 Hide ──
            //   ※여기서 면 짓기가 실패해도 <b>띠 선은 초록에 그대로 둔다</b>(검토 v100.0 · 낮음 4) — 선의 계산은 맞았고
            //     실패는 Civil이 경계를 받는 쪽이다. 명령줄 ⚠가 «띠는 그렸지만 순수옹벽_DH를 못 지었다»라고 밝힌다.
            // 순수옹벽_DH 하나 짓기 — 관문에 걸리면 지우고 던진다
            bool lastErased = true;
            DH.Grading.Core.CivilSafeMesh.Report? lastCs = null;       // mode 2가 지은 판의 다듬기 보고(판정 1·2)
            string BuildPure(System.Collections.Generic.List<Point3> ring, System.Collections.Generic.List<System.Collections.Generic.List<Point3>> holes,
                             string tag, int mode, out (double Max, int N, string At) ez, out string offNote)
            {
                // mode 0 — 옹벽 줄로 짓고 비파괴 경계 · 1 — 그 경계를 삼각망 변·꼭짓점에서 5µm 비켜 · 2 — ★초록 선으로 직접 자른 삼각형을 LandXML로
                ObjectId pid = ObjectId.Null;
                offNote = "";
                try
                {
                    TinSurface tin;
                    int nBExpect = 1 + holes.Count, xmlFaces = -1, xmlPts = -1, civPts = -1;
                    System.Collections.Generic.List<WallDaylight.Tri>? safe = null;
                    DH.Grading.Core.CivilSafeMesh.Report? cs = null;
                    if (mode == 2)
                    {
                        // ★★★[v100.3 · JACK «초록색선에 맞춰서 정확히 잘리지 않으면 이 기능은 의미가 없어 무조건 성공해야해»]
                        //   Civil에 자르기를 맡기지 않는다: 가상옹벽_DH 삼각형을 띠(초록 선 · 구멍 빼고)로 <b>직접</b> 잘라(Core ClipToBand — 1µm 격자로 한 번에 이음)
                        //   LandXML 면으로 넘긴다. Civil은 받기만 하므로 테두리 = 초록 선(1µm 안), 높이 = 옹벽 평면. 경계는 넣지 않는다(0개)
                        GradingBuilder.EraseSurfacesByBaseName(tr, PureName, groundId);
                        var clip = WallDaylight.ClipToBand(wallTris, ring, holes, out string cn, out int cbad);
                        if (clip.Count == 0 || cbad > 0) throw new SkipAttempt("띠를 삼각형으로 다 못 덮었다 — " + cn);
                        // ★★★[v100.4 · 0928 화면 없는 Civil 실측] 1µm 격자 그대로 넘기면 Civil이 0.1mm 안 점을 합치고 바늘 면을 뒤집어
                        //   (17:32: 775점 → 675 · 테두리 45mm) 관문에 걸려 톱니 길로 갔다 → Civil이 그대로 받는 삼각형으로 다듬어 넘긴다.
                        //   판정 1(정확) · 2(거의 — 자리를 적음) · 3(못 함 — 이 길을 건너뜀)
                        safe = DH.Grading.Core.CivilSafeMesh.Make(clip, ring, holes, wallTris, out cs);
                        sb.Append($"    (Civil에 맞게 다듬기: {cs.Summary})\n");
                        if (safe == null) throw new SkipAttempt("Civil이 그대로 받을 삼각형으로 못 다듬었다 — " + cs.Fail);
                        string xml = System.IO.Path.Combine(System.IO.Path.GetDirectoryName(DiagLog.FilePath) ?? ".", "DHWALLDL_순수옹벽.xml");
                        WallDaylight.WriteLandXmlTin(xml, "PUREWALL", safe, 1e-7, out int np, out int nf, out int nd);
                        if (nd > 0) throw new SkipAttempt($"넓이 0이라 뺀 면 {nd}개 — 면이 맞물리지 않을 수 있다");
                        try { ArchiveDump(xml); } catch { }                // ★[검토 0928 · 중간 5] 덮어써 잃지 않게
                        pid = TinSurface.CreateFromLandXML(db, PureName, xml, "PUREWALL");
                        if (pid.IsNull) throw new System.InvalidOperationException("CreateFromLandXML이 빈 ObjectId를 돌려줬다");
                        tin = (TinSurface)tr.GetObject(pid, OpenMode.ForWrite);
                        // ★[검토 v100.3 · 낮음 7] 지난 면을 못 지워 이름이 «_2»로 붙었으면 면이 둘이다 — 이 길은 접는다
                        if (tin.Name != PureName) throw new GateFail($"새 면 이름이 '{tin.Name}'이다(지난 '{PureName}'를 못 지움)");
                        // ★[검토 0928 · 중간 5] Civil이 점을 합쳤는가를 바로 보이는 숫자로
                        try { civPts = tin.GetGeneralProperties().NumberOfPoints; } catch { }
                        xmlFaces = nf; xmlPts = np;
                        nBExpect = 0;
                        offNote = $"{cn} · 다듬기 판정 {cs.Tier}({cs.TierText}) · LandXML 점 {np} · 면 {nf}";
                        sb.Append($"    (초록 선으로 직접 자름: {offNote} · {System.IO.Path.GetFileName(xml)})\n");
                    }
                    else
                    {
                    pid = GradingBuilder.BuildVirtualSlope(db, tr,
                        new System.Collections.Generic.List<System.Collections.Generic.List<Point3>>(),
                        PureName, rowsC!, groundId, null, midOrd: 0.001);
                    int bIn = GradingBuilder.LastIntended, bDef = GradingBuilder.LastDefined;
                    if (bIn < 0 || bDef < bIn) throw new System.InvalidOperationException($"브레이크라인을 {bIn}개 중 {bDef}개만 받았다");
                    tin = (TinSurface)tr.GetObject(pid, OpenMode.ForWrite);
                    if (mode == 1)
                    {
                        // ★[검토 v100.3 · 중간 3] 비킬 자리는 <b>Civil이 실제로 자를 이 면</b>의 삼각형으로 찾는다
                        //   (폴리곤 경계로 잘린 가상옹벽_DH 삼각형으로 찾으면 테두리(hull) 변과 속 변이 섞인다)
                        var own = new System.Collections.Generic.List<WallDaylight.Tri>();
                        using (var tc = tin.GetTriangles(true))
                            foreach (TinSurfaceTriangle t in tc)
                            {
                                try
                                {
                                    var a = t.Vertex1.Location; var b = t.Vertex2.Location; var c = t.Vertex3.Location;
                                    own.Add(new WallDaylight.Tri(new Point3(a.X, a.Y, a.Z), new Point3(b.X, b.Y, b.Z), new Point3(c.X, c.Y, c.Z)));
                                }
                                finally { t.Dispose(); }
                            }
                        ring = WallDaylight.OffTinEdges(ring, own, false, 5e-6, out int mv, out string mvAt, out double mvDz);
                        int mvH = 0; bool badH = false; double dzH = 0;
                        var hs = new System.Collections.Generic.List<System.Collections.Generic.List<Point3>>();
                        foreach (var h in holes)
                        {
                            hs.Add(WallDaylight.OffTinEdges(h, own, true, 5e-6, out int m2, out _, out double d2));
                            if (m2 < 0) badH = true; else mvH += m2;
                            dzH = System.Math.Max(dzH, d2);
                        }
                        holes = hs;
                        if (mv < 0 || badH) throw new SkipAttempt("비킨 링이 제 몸을 지른다");
                        if (mv + mvH == 0) throw new SkipAttempt("이 면의 삼각망에서 포갠 곳이 없다 — 첫 시도와 같다");
                        offNote = $"{mv + mvH}점 · 옮긴 점 높이 차(초록 선 대비) 최대 {System.Math.Max(mvDz, dzH) * 1000:F3}mm ·{mvAt}";
                    }
                    GradingBuilder.AddOuterBoundary(tin, ring, midOrd: 0.001);
                    foreach (var h in holes) GradingBuilder.AddHideBoundary(tin, h, midOrd: 0.001);
                    }
                    int nB = -1; try { nB = tin.BoundariesDefinition.Count; } catch { }
                    // ★[검토 v100.3 · 높음 1] 직접 자른 길은 경계가 없어 «덮인 자리»가 곧 새 면이다 — 덮인 자리로 잘라 견주면 덜 남긴 곳을 못 본다.
                    //   원래 띠와 바로 견준다
                    var (tri, sym, hd, perim, hdAt, vis) = Readback(tin, poly[0].X, poly[0].Y, ring, holes, clipToCover: mode != 2);
                    double dz = ZCheck(tr, tin, wallId, ring, holes, out int nz);
                    ez = EdgeZReadback(tin, ring, holes);
                    double symTol = 0.0002 * perim + 0.001;
                    string s0;
                    bool gateOk;
                    if (mode == 2)
                    {
                        // ★★[v100.4 · 검토 0928 · 중간 4·5] 직접 자른 길의 관문 — ①Civil이 <b>넘긴 그대로</b> 받았나: 점 수·삼각형 수가 같고,
                        //   꼭짓점 셋으로 같은 면을 세고, 다른 삼각형만 골라 넘긴 면과 높이를 정확히 견준다(«수 = 수»와 25점 대조로는 다시 이은 것을 못 가렸다 —
                        //   뒤집기는 수를 안 바꾸고, 꺾임을 가르는 뒤집기를 25점이 맞힐 확률은 0.3%). 모양은 넘긴 면과 견준다(뒤집다 면이 빠진 17:32 45mm가 여기서 걸린다).
                        //   ②넘긴 면이 초록 선에 맞나: 다듬기 판정(1 정확 / 2 거의)의 허용으로 띠·테두리·가상옹벽을 다시 본다.
                        var theirs = new System.Collections.Generic.List<WallDaylight.Tri>(vis.Count);
                        foreach (var t in vis) theirs.Add(new WallDaylight.Tri(t.A, t.B, t.C));
                        var cmp = DH.Grading.Core.CivilSafeMesh.Compare(safe!, theirs);
                        bool t1 = cs!.Tier == 1;
                        double planTol = t1 ? 1e-5 : 2e-4, zTol = t1 ? 1e-3 : 1e-2;
                        s0 = $"순수옹벽_DH — 직접 자른 삼각형(경계 {nB}개 · 기대 0) · 점 {civPts}/{xmlPts} · 삼각형 {tri}/{xmlFaces} · {cmp.Summary}"
                           + $" · 띠와 어긋난 넓이 {sym:F5}㎡(허용 {perim * (t1 ? 1e-5 : 2e-4):F4}) · 최대 거리 {hd * 1e6:F1}µm(허용 {planTol * 1e6:F0}µm{(hd > planTol ? " · " + hdAt : "")})"
                           + $" · 가상옹벽과 높이 차 최대 {dz * 1000:F2}mm({nz}점)"
                           + $" · <b>테두리 높이 − 띠 선 높이 최대 {ez.Max * 1000:F2}mm</b>({ez.N}점{(ez.N > 0 ? " @" + ez.At : "")})";
                        // ★[검토 0928 v100.4 · 낮음 4] 높이 허용을 따로 두면 «다듬은 면의 옹벽 오차 1mm + Civil 차이 1mm»로 2mm까지 통과한다 — 묶는다
                        gateOk = tri == xmlFaces && civPts == xmlPts && nB == 0 && cmp.Exceptions == 0
                              && cmp.SymArea <= 1e-8 && cmp.Hausdorff <= 1e-6 && cs.WallDz + cmp.MaxDz <= zTol
                              && sym <= perim * (t1 ? 1e-5 : 2e-4) && hd <= planTol && nz > 0 && dz <= zTol && ez.N > 0 && ez.Max <= zTol;
                    }
                    else
                    {
                        s0 = $"순수옹벽_DH — 경계 {nB}개(기대 {nBExpect}: Outer 1 + Hide {holes.Count}) · 보이는 삼각형 {tri}개"
                           + $" · 띠와 어긋난 넓이 {sym:F5}㎡(허용 {symTol:F4}) · 최대 거리 {hd * 1000:F2}mm(허용 1mm{(hd > 0.001 ? " · " + hdAt : "")})"
                           + $" · 가상옹벽과 높이 차 최대 {dz * 1000:F2}mm({nz}점)"
                           + $" · <b>테두리 높이 − 띠 선 높이 최대 {ez.Max * 1000:F2}mm</b>({ez.N}점{(ez.N > 0 ? " @" + ez.At : "")})";
                        // ★[계획 검토 0918 · 중간 4 · 검토 v100.0 · 중간 2] 넓이 차가 아니라 <b>모양 차</b>로 본다 — 비파괴 경계는 정확히 자르므로
                        //   기대 오차는 거의 0이다. 5mm는 1:0.01 면에서 높이 50cm라 헐거웠다 → 1mm · 둘레 비례.
                        //   견주는 띠는 <b>TIN이 덮는 자리로 잘라</b> 본다 — 1mm 스냅 때문에 밑선 꼭짓점이 폴리곤 밖 0.65mm까지 나가
                        //   그 자리는 Civil이 원래 못 보여 준다(현장 프로브: 0.005~0.0064㎡).
                        gateOk = !(tri <= 0 || nB != nBExpect || hd > 0.001 || sym > symTol || nz == 0 || dz > 0.001);
                    }
                    if (!gateOk)
                    {
                        // ★[v100.3 · JACK 0918 17:33] 관문에 걸리면 <b>Civil이 실제로 만든 삼각형</b>을 남긴다 — 어디를 어떻게 달리 잘랐는지
                        //   오프라인에서 잰다(17:32엔 «41mm»만 알고 자리를 몰랐다)
                        string dumpNote = "";
                        try { dumpNote = " · 삼각형을 " + System.IO.Path.GetFileName(DumpReadback(tag, ring, holes, vis)) + "에 남겼다"; } catch (System.Exception de) { dumpNote = $" · (삼각형 못 남김 {de.GetType().Name})"; }
                        throw new GateFail((mode == 2 ? "Civil이 넘긴 삼각형을 그대로 안 받았다 — " : "Civil이 경계를 띠대로 안 받았다 — ") + s0 + dumpNote);
                    }
                    if (mode == 2) lastCs = cs;
                    return s0;
                }
                catch
                {
                    // ★[검토 v100.3 · 낮음 6] 면을 만든 뒤 BuildVirtualSlope 안에서 던지면 pid가 비어 있다 — 이름으로 찾아 지운다.
                    //   지우지 못했으면 «(지웠다)»라고 적지 않는다
                    lastErased = false;
                    try
                    {
                        if (!pid.IsNull) { tr.GetObject(pid, OpenMode.ForWrite).Erase(); lastErased = true; }
                        else { GradingBuilder.EraseSurfacesByBaseName(tr, PureName, groundId); lastErased = true; }
                    }
                    catch { }
                    throw;
                }
            }
            // ★★★[v100.3 · JACK 0918 «초록색선에 맞춰서 정확히 잘리지 않으면 이 기능은 의미가 없어 무조건 성공해야해»] 네 번까지 짓는다 —
            //   ①<b>초록 선으로 직접 자른 삼각형(LandXML)</b> — Civil은 자르지 않고 받기만 한다(으뜸 길)
            //   ②정확한 링 + 비파괴 경계 ③그 경계를 삼각망 변·꼭짓점에서 5µm 비켜 ④1mm 격자 링(v100.1 · 테두리 수 cm 흔들림 — ⚠).
            //   17:32엔 ②가 되읽기 «최대 거리 41mm»로 걸려 곧장 ④로 가 톱니가 났다. 걸린 시도의 삼각형은 파일로 남는다.
            var tries = new System.Collections.Generic.List<(string Tag, string Label, int Mode, System.Collections.Generic.List<Point3> Ring,
                                                             System.Collections.Generic.List<System.Collections.Generic.List<Point3>> Holes)>();
            if (!r.UnsFallback) tries.Add(("직접", "초록 선으로 직접 자른 삼각형(LandXML)", 2, r.Ring, r.Holes));
            tries.Add(("정확", r.UnsFallback ? "1mm 격자 링" : "정확한 링 + 비파괴 경계", 0, r.Ring, r.Holes));
            if (!r.UnsFallback)
            {
                tries.Add(("비킴", "삼각망 변·꼭짓점에서 5µm 비킨 경계", 1, r.Ring, r.Holes));
                if (r.RingSnap != null) tries.Add(("격자", "1mm 격자 링", 0, r.RingSnap, r.HolesSnap));
            }
            string pure = "", ringNote = "", offNote = "";
            (double Max, int N, string At) ez = default;
            int used = -1; string lastWhy = "";
            for (int ti = 0; ti < tries.Count && used < 0; ti++)
            {
                try { pure = BuildPure(tries[ti].Ring, tries[ti].Holes, tries[ti].Tag, tries[ti].Mode, out ez, out offNote); used = ti; }
                catch (SkipAttempt sk)
                {
                    sb.Append($"    ({tries[ti].Label}은 건너뜀 — {sk.Message}{(lastErased ? "" : " · ⚠지은 면을 못 지웠다")})\n");
                }
                catch (System.Exception px)
                {
                    // ★[검토 v100.2 · 중간 3] 관문에 걸린 것과 짓기·되읽기 중 예외를 가려 적는다
                    lastWhy = px is GateFail ? px.Message : $"짓기·되읽기 중 예외 {px.GetType().Name}: {px.Message}(Civil이 못 받은 것이 아니라 확인을 못 했다)";
                    sb.Append($"    ⚠{tries[ti].Label}으로 순수옹벽_DH를 못 지었다({(lastErased ? "지웠다" : "<b>못 지웠다</b>")}) — {lastWhy}"
                              + (ti + 1 < tries.Count ? $" → {tries[ti + 1].Label}으로 다시 짓는다" : "") + "\n");
                }
            }
            if (used < 0)
            {
                Summaries.Add(head + $"⚠띠는 그렸지만 <b>순수옹벽_DH를 못 지었다</b> — {lastWhy} · " + r.Summary);
                return sb.ToString();
            }
            // ★[검토 v100.3 · 중간 2] 비킨 링이면 «1mm 안»이라고 박지 않고 옮긴 점의 높이 차를 <b>잰 값</b>으로 적는다
            if (tries[used].Tag == "직접")
                // ★[v100.4 · 계획 검토 0928 · 높음 2] «거의»는 자리와 크기를 밝힌다 — 0.1mm 안에 진짜 꺾임점 둘이 있거나 옹벽 꺾임점이 초록 선 곁이면
                //   Civil 해상도로는 원리상 10µm를 못 맞춘다(그래도 톱니 길 60mm보다 훨씬 낫다)
                // ★[검토 0928 v100.4 · 낮음 8] 까닭은 실제대로 — 해상도 탓만이 아니다(Civil이 고칠 면이 남았을 수도)
                ringNote = lastCs != null && lastCs.Tier == 2
                    ? $" · ⚠초록 선에 <b>거의</b> 맞춰 지었다({lastCs.Tier2Why})"
                    : " · <b>초록 선으로 직접 자른 삼각형</b>으로 지었다(LandXML · Civil에 맞게 다듬음)";
            else if (tries[used].Tag == "정확")
                ringNote = r.UnsFallback ? " · ⚠1mm 격자 링(정확한 자리로 못 되돌림)" : " · ⚠직접 자른 삼각형을 Civil이 못 받아 <b>정확한 링 + 비파괴 경계</b>로 지었다";
            else if (tries[used].Tag == "비킴")
                ringNote = $" · ⚠직접 자른 삼각형·정확한 링을 Civil이 못 받아 <b>삼각망 변·꼭짓점에서 5µm 비킨 경계</b>로 지었다({offNote})";
            else if (tries[used].Tag == "격자")
                ringNote = " · ⚠초록 선대로 못 지어 <b>1mm 격자 링</b>으로 지었다(테두리가 초록 선에서 수 cm 흔들린다)";
            // ★★[JACK 0918 «수직방향이 톱니처럼»] 화면의 노랑 테두리 = 보이는 삼각형의 테두리 꼭짓점 — 그 높이가 초록 선과 1cm 넘게 다르면 ⚠
            //   (면은 둔다 — 자리는 맞고 높이만 흔들린다). 못 쟀으면(0점) 그것도 ⚠
            string edgeNote = ez.N == 0 ? " · ⚠되읽은 테두리 높이를 못 쟀다"
                            : ez.Max > 0.01 ? $" · ⚠되읽은 테두리 높이가 띠 선에서 최대 {ez.Max * 100:F1}cm 벗어난다"
                            : $" · 되읽은 테두리 높이 차 최대 {ez.Max * 1000:F1}mm";
            string vis = "";
            try { vis = GradingBuilder.MakeSurfaceVisible(db, tr, PureName, "DH-순수옹벽면", "DH-순수옹벽", 6); } catch { }
            int hid = 0; try { hid = GradingBuilder.SetSurfaceVisible(tr, "가상옹벽_DH", false); } catch { }
            sb.Append($"    ★{pure}{ringNote} · 보이기: {vis} · 가상옹벽_DH 숨김 {hid}개\n");
            Summaries.Add(head + r.Summary + $" · <b>순수옹벽_DH 만듦</b>(어긋난 넓이 {sym3(pure)}{edgeNote}){ringNote}");
            return sb.ToString();
        }
        catch (System.Exception ex)
        {
            Summaries.Add(head + $"⚠못 만듦 — {ex.GetType().Name}: {ex.Message}");
            sb.Append($"  ⚠남길 띠 못 만듦 — {ex.GetType().Name}: {ex.Message}\n");
            return sb.ToString();
        }
        static string sym3(string s0) { int i = s0.IndexOf("어긋난 넓이 "); if (i < 0) return "?"; int j = s0.IndexOf('㎡', i); return j < 0 ? "?" : s0.Substring(i + 7, j - i - 7) + "㎡"; }
    }

    /// <summary>★[계획 검토 0918 · 중간 4] 순수옹벽_DH의 <b>보이는 삼각형 합집합</b>과 넘긴 띠(링 − 구멍)의 모양 차.
    /// 넓이 합만 견주면 남는 곳과 빠진 곳이 상쇄된다 — 대칭차 넓이와 양쪽 최대 거리를 잰다.</summary>
    private static (int Tri, double SymArea, double Hausdorff, double Perim, string HdAt,
                    System.Collections.Generic.List<(Point3 A, Point3 B, Point3 C)> Vis) Readback(TinSurface tin, double ox, double oy,
        System.Collections.Generic.IReadOnlyList<Point3> ring, System.Collections.Generic.IReadOnlyList<System.Collections.Generic.List<Point3>> holes,
        bool clipToCover = true)
    {
        var gf = new NetTopologySuite.Geometries.GeometryFactory();
        var vis = new System.Collections.Generic.List<(Point3 A, Point3 B, Point3 C)>();
        System.Collections.Generic.List<NetTopologySuite.Geometries.Geometry> Tris(bool includeHidden)
        {
            var polys = new System.Collections.Generic.List<NetTopologySuite.Geometries.Geometry>();
            using var tc = tin.GetTriangles(includeHidden);
            foreach (TinSurfaceTriangle t in tc)
            {
                try
                {
                    var a = t.Vertex1.Location; var b = t.Vertex2.Location; var c = t.Vertex3.Location;
                    if (!includeHidden) vis.Add((new Point3(a.X, a.Y, a.Z), new Point3(b.X, b.Y, b.Z), new Point3(c.X, c.Y, c.Z)));
                    var pg = gf.CreatePolygon(new[] {
                        new NetTopologySuite.Geometries.Coordinate(a.X - ox, a.Y - oy), new NetTopologySuite.Geometries.Coordinate(b.X - ox, b.Y - oy),
                        new NetTopologySuite.Geometries.Coordinate(c.X - ox, c.Y - oy), new NetTopologySuite.Geometries.Coordinate(a.X - ox, a.Y - oy) });
                    if (pg.Area > 0) polys.Add(pg);
                }
                finally { t.Dispose(); }
            }
            return polys;
        }
        var polysVis = Tris(false);
        if (polysVis.Count == 0) return (0, double.MaxValue, double.MaxValue, 0, "", vis);
        // ★[검토 v100.2 · 중간 3] 정확한 링은 테두리를 따라 아주 얇은 삼각형을 만든다 — 위상 예외에 강한 OverlayNGRobust로 합치고 견준다
        var u = NetTopologySuite.Operation.OverlayNG.OverlayNGRobust.Union(polysVis);
        NetTopologySuite.Geometries.Geometry? cover = null;
        try { cover = NetTopologySuite.Operation.OverlayNG.OverlayNGRobust.Union(Tris(true)); } catch { }
        NetTopologySuite.Geometries.LinearRing R(System.Collections.Generic.IReadOnlyList<Point3> r0)
        {
            var cs = new NetTopologySuite.Geometries.Coordinate[r0.Count + 1];
            for (int i = 0; i < r0.Count; i++) cs[i] = new NetTopologySuite.Geometries.Coordinate(r0[i].X - ox, r0[i].Y - oy);
            cs[r0.Count] = cs[0].Copy();
            return gf.CreateLinearRing(cs);
        }
        var holeRings = new NetTopologySuite.Geometries.LinearRing[holes.Count];
        for (int i = 0; i < holes.Count; i++) holeRings[i] = R(holes[i]);
        NetTopologySuite.Geometries.Geometry band = gf.CreatePolygon(R(ring), holeRings);
        double perim = band.Boundary.Length;
        // TIN이 덮는 자리로 잘라 본다(띠 꼭짓점이 TIN 밖 1mm 안에 있으면 Civil은 원래 못 보여 준다)
        if (clipToCover && cover != null) { try { band = NetTopologySuite.Operation.OverlayNG.OverlayNGRobust.Overlay(band, cover, NetTopologySuite.Operation.Overlay.SpatialFunction.Intersection); } catch { } }
        double sym;
        try { sym = NetTopologySuite.Operation.OverlayNG.OverlayNGRobust.Overlay(u, band, NetTopologySuite.Operation.Overlay.SpatialFunction.SymDifference).Area; } catch { sym = double.MaxValue; }
        // 양쪽 최대 거리 — 경계를 5mm 간격으로 훑는다
        // 가장 먼 점의 자리도 적는다(v100.3 — 17:32엔 «41mm»만 알고 자리를 몰랐다)
        (double M, double X, double Y) Far(NetTopologySuite.Geometries.Geometry from, NetTopologySuite.Geometries.Geometry to)
        {
            var idx = new NetTopologySuite.Operation.Distance.IndexedFacetDistance(to);
            double m = 0, mx = 0, my = 0;
            foreach (var g in NetTopologySuite.Geometries.Utilities.LinearComponentExtracter.GetLines(from))
            {
                var cs = g.Coordinates;
                for (int q = 0; q + 1 < cs.Length; q++)
                {
                    double L = cs[q].Distance(cs[q + 1]);
                    int ns = System.Math.Max(1, (int)System.Math.Ceiling(L / 0.005));
                    for (int k = 0; k <= ns; k++)
                    {
                        double x = cs[q].X + (cs[q + 1].X - cs[q].X) * k / ns, y = cs[q].Y + (cs[q + 1].Y - cs[q].Y) * k / ns;
                        double d = idx.Distance(gf.CreatePoint(new NetTopologySuite.Geometries.Coordinate(x, y)));
                        if (d > m) { m = d; mx = x; my = y; }
                    }
                }
            }
            return (m, mx, my);
        }
        var f1 = Far(band.Boundary, u.Boundary);      // 띠 테두리인데 Civil 면 테두리에서 먼 곳 — Civil이 덜 남겼다
        var f2 = Far(u.Boundary, band.Boundary);      // Civil 면 테두리인데 띠 테두리에서 먼 곳 — Civil이 더 남겼거나 구멍을 냈다
        double hd = System.Math.Max(f1.M, f2.M);
        string hdAt = f1.M >= f2.M ? $"띠 테두리 ({f1.X + ox:F3},{f1.Y + oy:F3})가 면 테두리에서 {f1.M * 1000:F1}mm"
                                   : $"면 테두리 ({f2.X + ox:F3},{f2.Y + oy:F3})가 띠 테두리에서 {f2.M * 1000:F1}mm";
        return (polysVis.Count, sym, hd, perim, hdAt, vis);
    }

    /// <summary>★[v100.3] 관문에 걸린 순수옹벽_DH의 <b>넘긴 링과 Civil이 만든 보이는 삼각형</b>을 파일로 — 오프라인에서 어디가 달랐는지 잰다.</summary>
    private static string DumpReadback(string tag, System.Collections.Generic.IReadOnlyList<Point3> ring,
        System.Collections.Generic.IReadOnlyList<System.Collections.Generic.List<Point3>> holes,
        System.Collections.Generic.List<(Point3 A, Point3 B, Point3 C)> vis)
    {
        string path = System.IO.Path.Combine(System.IO.Path.GetDirectoryName(DiagLog.FilePath) ?? ".", $"DHWALLDL_순수옹벽_되읽기_{tag}.txt");
        var ci = System.Globalization.CultureInfo.InvariantCulture;
        var w = new System.Text.StringBuilder();
        w.Append($"# DHPURE v1 {System.DateTime.Now:yyyy-MM-dd HH:mm:ss} {GradingSettings.Version} {tag}\n");
        void R(string name, System.Collections.Generic.IReadOnlyList<Point3> r0)
        {
            w.Append(name).Append(' ').Append(r0.Count).Append('\n');
            foreach (var q in r0) w.Append(string.Format(ci, "{0:R} {1:R} {2:R}\n", q.X, q.Y, q.Z));
        }
        R("RING", ring);
        foreach (var h in holes) R("HOLE", h);
        w.Append("TRI ").Append(vis.Count).Append('\n');
        foreach (var t in vis)
            w.Append(string.Format(ci, "{0:R} {1:R} {2:R} {3:R} {4:R} {5:R} {6:R} {7:R} {8:R}\n", t.A.X, t.A.Y, t.A.Z, t.B.X, t.B.Y, t.B.Z, t.C.X, t.C.Y, t.C.Z));
        System.IO.File.WriteAllText(path, w.ToString());
        try { ArchiveDump(path); } catch { }       // ★[검토 v100.3 · 낮음 7] 덮어써 잃지 않게 시각 붙여 보관
        return path;
    }

    /// <summary>관문(Civil이 경계를 띠대로 받았나)에 걸렸다 — 짓기·되읽기 중 예외와 가른다.</summary>
    private sealed class GateFail(string message) : System.Exception(message);
    /// <summary>이 시도는 할 까닭이 없다(비킬 곳이 없음 · 비킨 링이 무효) — 짓다 만 면은 지우고 다음 시도로.</summary>
    private sealed class SkipAttempt(string message) : System.Exception(message);

    /// <summary>★★[v100.2 · JACK 0918 <i>"또 수직방향이 톱니처럼 짤렸어 데이라잇에 깔끔하게 안잘려"</i>] 순수옹벽_DH <b>테두리 높이</b> 되읽기.
    /// <para>화면의 테두리(노랑)는 보이는 삼각형의 테두리 꼭짓점을 잇는 선이다(스샷 15:53 — 꺾임점이 삼각형 변 위). 그 꼭짓점 중
    /// 띠 경계에서 2µm 안인 것마다 <b>Civil이 쓴 높이</b>와 띠 선 높이(가장 가까운 변 위로 옮겨 보간 — 검토 v100.2 Q1)의 차를 잰다.</para>
    /// <para>되읽기는 화면을 정하는 값을 잰다 — 계산(⑦-c)이 맞다고 해도 Civil이 다르게 자르면 여기서 드러난다.</para></summary>
    private static (double Max, int N, string At) EdgeZReadback(TinSurface tin,
        System.Collections.Generic.IReadOnlyList<Point3> ring, System.Collections.Generic.IReadOnlyList<System.Collections.Generic.List<Point3>> holes)
    {
        double ox = ring[0].X, oy = ring[0].Y;
        var segs = new System.Collections.Generic.List<(double X1, double Y1, double Z1, double X2, double Y2, double Z2)>();
        var tree = new NetTopologySuite.Index.Strtree.STRtree<int>();
        foreach (var r0 in new[] { ring }.Concat(holes))
            for (int k = 0; k < r0.Count; k++)
            {
                var a = r0[k]; var b = r0[(k + 1) % r0.Count];
                tree.Insert(new NetTopologySuite.Geometries.Envelope(a.X - ox, b.X - ox, a.Y - oy, b.Y - oy), segs.Count);
                segs.Add((a.X - ox, a.Y - oy, a.Z, b.X - ox, b.Y - oy, b.Z));
            }
        if (segs.Count == 0) return (0, 0, "");
        tree.Build();
        // ★[검토 v100.2 · 낮음 5] 10µm — Civil이 경계에 넣은 점은 링 위 1µm 안이다. 0.1mm로 잡으면 경계 곁 원래 TIN 점이 걸려
        //   1:0.001 폐합면에서 가짜 ⚠(최대 10cm)가 뜬다
        const double Tol = 2e-6;           // ★[검토 v100.3 · 낮음 5] 비킴 거리(10µm)와 겹치지 않게 2µm
        double max = 0; int n = 0; string at = "";
        var seen = new System.Collections.Generic.HashSet<(long, long)>();
        try
        {
            using var tc = tin.GetTriangles(false);
            foreach (TinSurfaceTriangle t in tc)
            {
                try
                {
                    foreach (var v in new[] { t.Vertex1.Location, t.Vertex2.Location, t.Vertex3.Location })
                    {
                        double x = v.X - ox, y = v.Y - oy;
                        if (!seen.Add(((long)System.Math.Round(x * 1e6), (long)System.Math.Round(y * 1e6)))) continue;
                        double best = double.MaxValue, zr = 0;
                        foreach (int i in tree.Query(new NetTopologySuite.Geometries.Envelope(x - Tol, x + Tol, y - Tol, y + Tol)))
                        {
                            var s = segs[i];
                            double ex = s.X2 - s.X1, ey = s.Y2 - s.Y1, L2 = ex * ex + ey * ey;
                            double u = L2 < 1e-24 ? 0 : System.Math.Max(0, System.Math.Min(1, ((x - s.X1) * ex + (y - s.Y1) * ey) / L2));
                            double dx = x - (s.X1 + ex * u), dy = y - (s.Y1 + ey * u), d = System.Math.Sqrt(dx * dx + dy * dy);
                            if (d < best) { best = d; zr = s.Z1 + (s.Z2 - s.Z1) * u; }
                        }
                        if (best > Tol) continue;
                        n++;
                        double g = System.Math.Abs(v.Z - zr);
                        if (g > max) { max = g; at = $"({v.X:F3},{v.Y:F3}) 테두리 {v.Z:F4} · 선 {zr:F4}"; }
                    }
                }
                finally { t.Dispose(); }
            }
        }
        catch (System.Exception ex) { at = $"(되읽기 중 예외 {ex.GetType().Name})"; }
        return (max, n, at);
    }

    /// <summary>★[검토 v100.0 · 중간 2] 띠 안 몇 점에서 <b>순수옹벽_DH 높이 = 가상옹벽_DH 높이</b>인가 — 같은 줄로 지었으니 같아야 한다.
    /// 구멍 안과 띠 밖은 뺀다. 가장 큰 차를 돌려준다(못 잰 점은 세지 않는다).</summary>
    private static double ZCheck(Transaction tr, TinSurface pure, ObjectId wallId,
        System.Collections.Generic.IReadOnlyList<Point3> ring, System.Collections.Generic.IReadOnlyList<System.Collections.Generic.List<Point3>> holes, out int n)
    {
        n = 0; double worst = 0;
        try
        {
            var wall = (TinSurface)tr.GetObject(wallId, OpenMode.ForRead);
            var gf = new NetTopologySuite.Geometries.GeometryFactory();
            NetTopologySuite.Geometries.LinearRing R(System.Collections.Generic.IReadOnlyList<Point3> r0)
            {
                var cs = new NetTopologySuite.Geometries.Coordinate[r0.Count + 1];
                for (int i = 0; i < r0.Count; i++) cs[i] = new NetTopologySuite.Geometries.Coordinate(r0[i].X, r0[i].Y);
                cs[r0.Count] = cs[0].Copy();
                return gf.CreateLinearRing(cs);
            }
            var hr = new NetTopologySuite.Geometries.LinearRing[holes.Count];
            for (int i = 0; i < holes.Count; i++) hr[i] = R(holes[i]);
            var band = gf.CreatePolygon(R(ring), hr);
            var inner = band.Buffer(-0.002);                    // 경계 바로 위는 뺀다(절벽이라 1mm에 10cm)
            if (inner.IsEmpty) inner = band;
            var pts = new System.Collections.Generic.List<NetTopologySuite.Geometries.Coordinate> { inner.InteriorPoint.Coordinate };
            var env = inner.EnvelopeInternal;
            var prep = NetTopologySuite.Geometries.Prepared.PreparedGeometryFactory.Prepare(inner);
            for (int gx = 0; gx < 40 && pts.Count < 25; gx++)
                for (int gy = 0; gy < 40 && pts.Count < 25; gy++)
                {
                    var c = new NetTopologySuite.Geometries.Coordinate(env.MinX + env.Width * (gx + 0.5) / 40, env.MinY + env.Height * (gy + 0.5) / 40);
                    if (prep.Contains(gf.CreatePoint(c))) pts.Add(c);
                }
            foreach (var c in pts)
            {
                try { double zp = pure.FindElevationAtXY(c.X, c.Y), zw = wall.FindElevationAtXY(c.X, c.Y); worst = System.Math.Max(worst, System.Math.Abs(zp - zw)); n++; }
                catch { }
            }
        }
        catch { }
        return worst;
    }

    /// <summary>상대면 하나 — 입력 떨구기 · 계산 · 그리기 · 되읽기.</summary>
    private static string RunOne(Database db, Transaction tr, Kind k, System.Collections.Generic.List<Point3> poly,
        System.Collections.Generic.List<WallDaylight.Tri> wallTris, System.Collections.Generic.List<WallDaylight.Tri> targetTris,
        string targetName, bool bladeOk, string bladeNote, string extra)
    {
        var sb = new System.Text.StringBuilder();
        try
        {
            // 입력을 떨군다 — 하네스 S138 ⑥이 <b>같은 입력</b>으로 다시 돌린다(검토 0918 · 높음 4)
            string dump;
            try
            {
                dump = System.IO.Path.Combine(System.IO.Path.GetDirectoryName(DiagLog.FilePath) ?? ".", k.DumpName);
                WallDaylight.WriteInput(dump, poly, wallTris, targetTris,
                    $"{System.DateTime.Now:yyyy-MM-dd HH:mm:ss} {GradingSettings.Version} {k.Tag}='{targetName}'");
                ArchiveDump(dump);
            }
            catch (System.Exception de) { dump = $"(못 떨굼: {de.GetType().Name})"; }

            var r = WallDaylight.Build(poly, wallTris, targetTris, targetName);
            sb.Append($"  ── [{k.Tag}] ──{(extra.Length > 0 ? " " + extra : "")}\n");
            sb.Append(r.Trace);
            sb.Append($"    입력 파일: {dump}\n");

            bool broken = r.Broken || !bladeOk;             // 판정은 Core 한 곳(검토 0918 · 중간 1)
            if (!bladeOk) sb.Append($"    ⚠옹벽 면이 성하지 않다 — {bladeNote} → 확인용 레이어로 보낸다\n");
            // 지운 선 메모는 <b>한 번만</b>(원지반 쪽) — 두 줄에 다 붙으면 같은 말이 두 번 나온다
            string eNote = k == Ground ? EraseNote : "";
            if (eNote.Length > 0) sb.Append($"    {eNote}\n");
            string head = $"[{k.Tag}] ";
            if (r.Ring == null)
            {
                Summaries.Add(head + r.Summary + (eNote.Length > 0 ? " · " + eNote : ""));
                return sb.ToString();
            }
            string layer = broken ? k.LayerSuspect : k.Layer;
            short aci = broken ? (short)2 : k.Aci;          // 확인용은 노랑
            ObjectId id;
            try { id = DrawClosed(db, tr, r.Ring, layer, aci); }
            catch (System.Exception dx)
            {
                string m = $"⚠데이라잇을 계산했지만 <b>못 그렸다</b> — {dx.GetType().Name}: {dx.Message}";
                Summaries.Add(head + m);
                sb.Append("    " + m + "\n");
                return sb.ToString();
            }
            // ★되읽기 — 화면에 남는 것이 정말 닫혔고 점 수가 맞는가(마지막 쓰기 뒤의 값을 잰다)
            string rb;
            try
            {
                var pl = (Polyline3d)tr.GetObject(id, OpenMode.ForRead);
                int nv = 0;
                foreach (ObjectId vid in pl) nv++;
                rb = $"되읽기: 닫힘 {(pl.Closed ? "예" : "<b>아니오</b>")} · 정점 {nv}개(기대 {r.Ring.Count}) · 레이어 '{pl.Layer}'";
                if (!pl.Closed || nv != r.Ring.Count)
                {
                    rb = "⚠" + rb;
                    // ★되읽기가 틀렸으면 정식 레이어에 두지 않는다(검토 0918 · 낮음 3)
                    if (!broken)
                    {
                        pl.UpgradeOpen();
                        pl.LayerId = GradingBuilder.EnsureLayer(db, tr, k.LayerSuspect, 2);
                        broken = true;
                        rb += " → 확인용 레이어로 옮겼다";
                    }
                }
            }
            catch (System.Exception re) { rb = $"⚠되읽기 실패 {re.GetType().Name}"; }
            sb.Append($"    {rb}\n");
            Summaries.Add(head
                + (broken ? $"⚠데이라잇이 <b>틀린 곳이 있어</b> 확인용 레이어 '{k.LayerSuspect}'(노랑)에만 그렸다 — " : "")
                + r.Summary + (rb.StartsWith("⚠") ? " · " + rb : "")
                + (!bladeOk ? " · " + bladeNote : "")
                + (eNote.Length > 0 ? " · " + eNote : ""));
            return sb.ToString();
        }
        catch (System.Exception ex)
        {
            string m = $"⚠데이라잇 못 만듦 — {ex.GetType().Name}: {ex.Message}";
            Summaries.Add($"[{k.Tag}] " + m);
            sb.Append("  " + m + "\n");
            return sb.ToString();
        }
    }

    /// <summary>★[JACK 0918] 입력 파일을 <b>시각 붙여</b> <c>진단이력</c>에도 남긴다 — 최근 20판만.
    /// <para>12:47 절토 판의 입력이 13:02 성토 판에 <b>덮여 사라졌다</b>(재생할 수 없게 됐다).
    /// <c>DiagLog.Archive</c>에 끼우지 않는 까닭: 그것은 <b>다음 실행이 시작될 때</b> 직전 로그를 옮기는데,
    /// 그때 입력 파일은 이미 이번 판 것이라 <b>짝이 어긋난다</b>. 쓰는 그 자리에서 바로 남긴다.</para></summary>
    /// <summary>[v101.0] 옹벽 합성(DHWALLCOMP)도 같은 보관을 쓴다.</summary>
    internal static void ArchiveDumpPublic(string dump) => ArchiveDump(dump);

    private static void ArchiveDump(string dump)
    {
        try
        {
            string dir = System.IO.Path.GetDirectoryName(dump) ?? ".";
            string hist = System.IO.Path.Combine(dir, "진단이력");
            System.IO.Directory.CreateDirectory(hist);
            string stem = System.IO.Path.GetFileNameWithoutExtension(dump);
            // ★[검토 0928 v100.4 · 낮음 9] 원래 확장자를 지킨다(순수옹벽 xml이 .txt로 보관되던 것)
            string ext = System.IO.Path.GetExtension(dump);
            if (string.IsNullOrEmpty(ext)) ext = ".txt";
            string dst = System.IO.Path.Combine(hist, $"{stem}_{System.DateTime.Now:yyyyMMdd_HHmmss}{ext}");
            if (!System.IO.File.Exists(dst)) System.IO.File.Copy(dump, dst);
            var files = new System.IO.DirectoryInfo(hist).GetFiles(stem + "_2*" + ext);   // 날짜로 시작하는 것만(_계획 파일과 안 섞이게)
            if (files.Length > 20)
            {
                System.Array.Sort(files, (a, b) => a.LastWriteTime.CompareTo(b.LastWriteTime));
                for (int i = 0; i < files.Length - 20; i++) try { files[i].Delete(); } catch { }
            }
        }
        catch { }   // 보관은 곁일이다 — 못 해도 본 작업은 간다(입력 파일 자체는 이미 썼다)
    }

    private static ObjectId DrawClosed(Database db, Transaction tr, System.Collections.Generic.IReadOnlyList<Point3> ring,
        string layer, short aci)
    {
        var layerId = GradingBuilder.EnsureLayer(db, tr, layer, aci);
        var bt = (BlockTable)tr.GetObject(db.BlockTableId, OpenMode.ForRead);
        var ms = (BlockTableRecord)tr.GetObject(bt[BlockTableRecord.ModelSpace], OpenMode.ForWrite);
        var pl = new Polyline3d { LayerId = layerId };
        ms.AppendEntity(pl);
        tr.AddNewlyCreatedDBObject(pl, true);
        try
        {
            foreach (var q in ring)    // 첫 점을 끝에 되풀이하지 않는다 — 닫힘은 Closed가 한다
            {
                var v = new PolylineVertex3d(new Autodesk.AutoCAD.Geometry.Point3d(q.X, q.Y, q.Z));
                pl.AppendVertex(v);
                tr.AddNewlyCreatedDBObject(v, true);
            }
            pl.Closed = true;          // JACK: «닫힘: 예»
        }
        catch
        {
            // ★반쯤 그린 열린 선이 정식 레이어에 남지 않게 지운다(검토 0918 · 낮음 3)
            try { pl.Erase(); } catch { }
            throw;
        }
        return pl.ObjectId;
    }
}
