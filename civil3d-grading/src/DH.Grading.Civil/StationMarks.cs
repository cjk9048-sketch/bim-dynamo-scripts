using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.Geometry;
using CivilDb = Autodesk.Civil.DatabaseServices;

namespace DH.Grading.Civil;

/// <summary>★[JACK 0810] <b>측점 목록 — 종단도와 횡단도가 같은 측점을 쓰게 하는 한 곳.</b>
///
/// <para>JACK의 세 가지 요구는 사실 하나로 모인다:
/// ① 정체인 외에 이형관 위치(수평·수직)에서 자동으로 체인이 끊어질 것
/// ② 밸브실처럼 원하는 자리를 수동으로 더할 것
/// ③ 단면검토선으로 옮길 때 위 측점이 모두 따라올 것.
/// 셋 다 <b>'이 노선의 특별한 측점 목록'</b> 하나를 놓고 벌어지는 일이다. 그래서
/// <b>모으는 쪽(수집기)</b>과 <b>쓰는 쪽(종단도 밴드·단면검토선)</b>을 갈라 놓는다 —
/// 나중에 관로 애드인을 만들 때 수집기에 '관 계획고 종단의 꺾임점' 하나만 더 꽂으면 되고
/// 쓰는 쪽은 손대지 않는다(JACK: "별도로 관로 애드인을 만들 계획인데 그때 이것이 필요하다").</para>
///
/// <para><b>왜 보이지 않게 저장하는가.</b> JACK 지시 — "숨겨줘". 도면에 마커를 그려 두면
/// 눈에는 보이지만 남의 도면을 어지럽히고 실수로 지워지거나 옮겨진다. 그래서 <b>선형에 딸린
/// 확장 사전(도면에 함께 저장되지만 그려지지 않는 자리)</b>에 측점 값만 적어 둔다.
/// 선형을 지우면 같이 사라지고, 도면을 남에게 줘도 따라간다.
/// 대신 '어디에 넣었는지'를 볼 수 없으므로 <b>목록을 명령창에 찍어 주는 것이 필수</b>다.</para>
///
/// <para><b>꺾임점은 선형에 되묻지 않는다.</b> 노선을 선형으로 바꿀 때 모서리에 곡선이 끼면
/// 꼭짓점 하나가 곡선 시·종점 <b>두 점</b>으로 갈린다(JACK 지적: "pvi로 잡으면 이형관의
/// 앞뒤가 잡히는 거 아니야?"). 그런데 우리는 <b>사용자가 찍은 점을 이미 손에 쥐고 있다</b> —
/// 그 점의 측점을 직접 재면 곡선이 끼든 말든 <b>이형관 하나당 측점 하나</b>다.</para></summary>
public static class StationMarks
{
    /// <summary>선형 확장 사전 안에서 이 목록이 사는 자리.</summary>
    private const string DictKey = "DH_측점목록";

    /// <summary>★★[v29.0 점검 반영 · JACK 0811 확정] <b>같은 자리만 합친다 — 솎아내지 않는다.</b>
    ///
    /// <para>종전 값은 <b>0.5m</b>였다. 그러면 굴곡부가 20.4m에 있을 때 <c>No.1</c>(20.00m)
    /// <b>정측점이 지워진다</b> — 도면에서 기준이 되는 번호가 사라지는 것이라 가장 나쁜 종류의 누락이다.
    /// JACK 확정: <i>"최소간격 없어 둘 다 찍어."</i> 겹쳐 보이는 것보다 빠지는 것이 나쁘다.</para>
    ///
    /// <para>그래서 이 값은 <b>솎아내기 기준이 아니라 '같은 점' 판정</b>이다 — 1cm.
    /// 종단도(DHPROFILE)는 이미 1cm를 쓰고 있었는데 <see cref="SampleLineCommand"/>는 기본값 0.5m를
    /// 쓰고 있어서 <b>같은 노선인데 두 명령의 측점 목록이 달랐다</b>. 기본값을 맞춘다.</para></summary>
    public const double MergeTol = 0.01;

    /// <summary>계획면과 원지반이 이보다 가까우면 <b>겹친다(정지 안 한 데)</b>고 본다.
    /// <para>값은 <b>실제 거리(m)</b>다 — 축척과 무관해야 한다. 종단·횡단이 같은 측점을 써야 하는데
    /// 축척에 따라 집합이 달라지면 두 도면이 어긋난다.</para></summary>
    public const double PadGroundTol = 0.05;

    /// <summary>측점 하나. <paramref name="Why"/>는 사람이 읽을 사유(밸브실·이형관·구배변화 등).</summary>
    public readonly record struct Mark(double Station, string Why);

    // ── 저장·읽기 ────────────────────────────────────────────────────────────

    /// <summary>선형에 붙여 둔 수동 측점 목록을 읽는다(없으면 빈 목록).</summary>
    public static List<Mark> Load(Transaction tr, ObjectId alignId)
    {
        var list = new List<Mark>();
        try
        {
            var obj = tr.GetObject(alignId, OpenMode.ForRead);
            if (obj.ExtensionDictionary.IsNull) return list;
            var d = (DBDictionary)tr.GetObject(obj.ExtensionDictionary, OpenMode.ForRead);
            if (!d.Contains(DictKey)) return list;
            if (tr.GetObject(d.GetAt(DictKey), OpenMode.ForRead) is not Xrecord xr || xr.Data == null) return list;
            double st = double.NaN;
            foreach (TypedValue tv in xr.Data)
            {
                if (tv.TypeCode == (short)DxfCode.Real) st = Convert.ToDouble(tv.Value);
                else if (tv.TypeCode == (short)DxfCode.Text && !double.IsNaN(st))
                { list.Add(new Mark(st, tv.Value?.ToString() ?? "")); st = double.NaN; }
            }
        }
        catch { }
        return list;
    }

    /// <summary>수동 측점 목록을 선형에 적어 둔다. 반환=성공 여부.</summary>
    public static bool Save(Transaction tr, ObjectId alignId, IEnumerable<Mark> marks)
    {
        try
        {
            var obj = tr.GetObject(alignId, OpenMode.ForWrite);
            if (obj.ExtensionDictionary.IsNull) obj.CreateExtensionDictionary();
            var d = (DBDictionary)tr.GetObject(obj.ExtensionDictionary, OpenMode.ForWrite);
            var rb = new ResultBuffer();
            foreach (var m in marks.OrderBy(x => x.Station))
            {
                rb.Add(new TypedValue((short)DxfCode.Real, m.Station));
                rb.Add(new TypedValue((short)DxfCode.Text, m.Why ?? ""));
            }
            var xr = new Xrecord { Data = rb };
            d.SetAt(DictKey, xr);          // 같은 이름이 있으면 갈아 끼운다
            tr.AddNewlyCreatedDBObject(xr, true);
            return true;
        }
        catch { return false; }
    }

    // ── 모으는 쪽(수집기) ────────────────────────────────────────────────────

    /// <summary>화면의 한 점이 노선의 몇 측점인지. 노선에서 너무 멀면 null.</summary>
    public static double? StationOf(CivilDb.Alignment al, Point3d p, double maxOffset = 1e9)
    {
        try
        {
            double st = 0, off = 0;
            al.StationOffset(p.X, p.Y, ref st, ref off);
            if (Math.Abs(off) > maxOffset) return null;
            if (st < al.StartingStation - 1e-6 || st > al.EndingStation + 1e-6) return null;
            return st;
        }
        catch { return null; }
    }

    /// <summary>노선을 그릴 때 <b>사용자가 찍은 꼭짓점</b>을 측점으로 바꾼다(수평 이형관).
    /// 시작·끝점은 뺀다 — 그건 꺾임이 아니라 노선의 끄트머리다.</summary>
    public static List<Mark> FromRouteVertices(CivilDb.Alignment al, IReadOnlyList<Point3d> pts)
    {
        var list = new List<Mark>();
        for (int i = 1; i < pts.Count - 1; i++)
        {
            var st = StationOf(al, pts[i]);
            if (st.HasValue) list.Add(new Mark(st.Value, "꺾임점"));
        }
        return list;
    }
    // ★★[v30.3 · JACK 0812] 여기 있던 <b>FromProfileGradeBreaks(계획 종단의 구배변화점)</b>은 걷어냈다.
    //   JACK: <i>"우리 굴곡부라는 개념은 아예 버리기로 하지 않았어?"</i> — 맞다.
    //   종단의 PVI를 훑어 '많이 꺾인 것'을 고르는 방식은 <b>지표면 표본점과 설계 변화를 구분하지 못한다</b>
    //   (62m 노선에서 78개가 잡혔다). 허용오차를 아무리 다듬어도 원리상 갈리지 않는다.
    //   측점은 이제 <b>선과의 교차</b>로만 잡는다 — 데이라잇·소단·사면(아래 Crossings 계열).

    // ── 측점 = 선형이 <b>정지 경계선</b>을 넘는 자리 ──────────────────────────
    //
    //   ★★[v30.3 · JACK 0812] <b>'굴곡부'라는 말도 개념도 버렸다.</b>
    //   측점은 '많이 꺾인 곳을 찾는' 것이 아니라 <b>선을 넘는 자리를 계산하는</b> 것이다.
    //   대상 선은 정지면을 만드는 데 쓰인 것 그대로 — <b>데이라잇 · 소단선 · 사면선 · 옹벽선</b>.
    //   추정이 아니라 계산이므로 허용오차가 필요 없고, 값이 아니라 <b>자리</b>만 쓴다.
    //
    //   근거는 <b>번들</b>에서 온다(도면에 그려져 있든 말든). 도면의 선은 그릴 때 레이어를 지우므로
    //   누적 구역에서 <b>마지막 것만</b> 남을 수 있다 — 그걸 근거로 삼으면 순서에 매인다.
    //   JACK: <i>"어느 순간엔 뭐 해야 하고 하는 식이면 제약이 생기고 범용성이 떨어져."</i>

    /// <summary>선 하나(정점 목록)가 선형을 넘는 자리를 모은다 — 굴곡선·도면선 공용.
    /// <para>Civil의 선형은 AcDb 곡선이 아니라 <c>IntersectWith</c>가 없다. 대신
    /// <c>StationOffset</c>이 주는 <b>부호 있는 이격</b>을 쓴다 — 한 구간의 양 끝에서 부호가 바뀌면
    /// 그 사이에서 선형을 넘은 것이고, 이분법으로 0이 되는 자리를 좁힌다. 곡선 선형에도 그대로 통한다.</para></summary>
    private static int Crossings(CivilDb.Alignment al, IList<Point3d> vs, string why,
                                 double s0, double s1, Func<double, bool> keep,
                                 List<Mark> outp, ref int nSeg, ref int nSkip, ref int nOutside,
                                 ref int nTrim)
    {
        bool Probe(Point3d p, out double st, out double off)
        {
            st = 0; off = 0;
            try { al.StationOffset(p.X, p.Y, ref st, ref off); return true; }
            catch { return false; }
        }

        static Point3d Along(Point3d a, Point3d b, double t)
            => new(a.X + (b.X - a.X) * t, a.Y + (b.Y - a.Y) * t, 0.0);

        // ★★[v32.1 · JACK 0812] <b>한쪽 끝이 안 재진다고 선분을 통째로 버리지 않는다 — 기점·종점이 그래서 빠졌다.</b>
        //
        //   <c>StationOffset</c>은 점의 수직 투영이 <b>선형의 측점 범위를 벗어나면 예외</b>를 던진다.
        //   그런데 <b>기점·종점의 교차는 바로 그 언저리</b>에서 일어난다 — 경계선이 노선 시작점을
        //   비스듬히 넘어가면 바깥쪽 끝은 '시작보다 뒤'라 못 재고, <b>그 선분이 통째로 버려졌다</b>.
        //   교차 자체는 노선 안(예: 0.5m)에 있는데도 <b>기점 측점이 영영 안 잡힌</b> 것이다.
        //   (§25 열린 결함: "한쪽 끝이 노선 범위 밖이면 구간을 통째로 버린다 — 기점·종점 누락 가능")
        //
        //   → 버리지 말고 <b>잴 수 있는 데까지 줄인다.</b> 되는 끝에서 안 되는 끝 쪽으로 이분해
        //     <b>마지막으로 재지는 자리</b>를 찾아 그 점을 끝으로 삼는다.
        //     판정은 <b>실제로 잰 값으로만</b> 하므로 없는 교차를 만들어 내지 않는다 —
        //     줄인 구간 안에서 부호가 바뀌면 그건 선형을 진짜로 넘은 것이다.
        bool Shrink(Point3d good, Point3d bad, out Point3d edge, out double offEdge)
        {
            edge = good; offEdge = 0.0;
            if (!Probe(good, out _, out offEdge)) return false;
            double lo = 0.0, hi = 1.0;                 // lo = 재지는 쪽 · hi = 안 재지는 쪽
            for (int it = 0; it < 30; it++)
            {
                double t = (lo + hi) / 2.0;
                var P = Along(good, bad, t);
                if (Probe(P, out _, out double o)) { lo = t; edge = P; offEdge = o; }
                else hi = t;
            }
            return true;
        }

        int hit = 0;
        for (int k = 1; k < vs.Count; k++)
        {
            nSeg++;
            Point3d A = vs[k - 1], B = vs[k];
            bool okA = Probe(A, out _, out double oA);
            bool okB = Probe(B, out _, out double oB);

            if (!okA && !okB) { nSkip++; continue; }   // 양 끝 다 못 잼 — 판정할 근거가 없다
            if (!okB)
            {
                if (!Shrink(A, B, out Point3d eB, out double oEdge)) { nSkip++; continue; }
                B = eB; oB = oEdge; nTrim++;
            }
            else if (!okA)
            {
                if (!Shrink(B, A, out Point3d eA, out double oEdge)) { nSkip++; continue; }
                A = eA; oA = oEdge; nTrim++;
            }

            if (oA == 0.0 && oB == 0.0) continue;      // 선형 위에 겹쳐 누운 구간 — 넘은 게 아니다
            if (oA * oB > 0) continue;                 // 같은 쪽 → 안 넘었다

            double lo = 0.0, hi = 1.0, sHit = double.NaN;
            for (int it = 0; it < 40; it++)
            {
                double t = (lo + hi) / 2.0;
                var P = new Point3d(A.X + (B.X - A.X) * t, A.Y + (B.Y - A.Y) * t, 0);
                if (!Probe(P, out double stM, out double oM)) break;
                sHit = stM;
                if (oM == 0.0) break;
                if (oA * oM > 0) lo = t; else hi = t;
            }
            if (double.IsNaN(sHit)) continue;
            if (sHit < s0 - 1e-6 || sHit > s1 + 1e-6) { nSkip++; continue; }   // 선형 밖으로 연장된 자리
            if (keep != null && !keep(sHit)) { nOutside++; continue; }
            outp.Add(new Mark(sHit, why)); hit++;
        }
        return hit;
    }

    /// <summary>같은 자리(1cm 이내)를 하나로 합친다. <b>솎아내기가 아니라 중복 제거</b>다 —
    /// 절토·성토 두 면이 같은 링에서 만들어져 같은 교차가 두 번 잡히는 경우를 위한 것이다.</summary>
    public static int Dedupe(List<Mark> list)
    {
        list.Sort((a, b) => a.Station.CompareTo(b.Station));
        int dup = 0;
        for (int i = list.Count - 1; i > 0; i--)
            if (list[i].Station - list[i - 1].Station < 0.01) { list.RemoveAt(i); dup++; }
        return dup;
    }

    public static List<Mark> FromGradingBreaklines(Transaction tr, CivilDb.Alignment al,
                                                   IEnumerable<ObjectId> surfIds,
                                                   Func<double, bool> keep,
                                                   System.Text.StringBuilder log)
    {
        var list = new List<Mark>();
        if (al == null || surfIds == null) return list;
        double s0 = al.StartingStation, s1 = al.EndingStation;

        int nSurf = 0, nBl = 0, nSeg = 0, nSkipFar = 0, nOutside = 0, nTrim = 0;
        foreach (ObjectId sid in surfIds)
        {
            if (sid.IsNull) continue;
            string nm = "?";
            int hitHere = 0, blHere = 0, segBefore = nSeg, outBefore = nOutside;
            try
            {
                if (tr.GetObject(sid, OpenMode.ForRead) is not CivilDb.TinSurface tin) continue;
                nm = tin.Name; nSurf++;
                var defs = tin.BreaklinesDefinition;
                for (int i = 0; i < defs.Count; i++)
                {
                    CivilDb.SurfaceOperationAddBreakline op;
                    try { op = defs[i]; } catch { continue; }
                    foreach (CivilDb.SurfaceBreakline bl in op)
                    {
                        blHere++;
                        Point3dCollection vs;
                        try { vs = bl.Vertices; } catch { continue; }
                        var pts = new List<Point3d>(vs.Count);
                        foreach (Point3d p in vs) pts.Add(p);
                        hitHere += Crossings(al, pts, "굴곡부·" + nm, s0, s1, keep, list,
                                             ref nSeg, ref nSkipFar, ref nOutside, ref nTrim);
                    }
                }
            }
            catch (System.Exception ex) { log?.AppendLine($"   굴곡선 '{nm}' 읽기 실패 — {ex.Message}"); }
            nBl += blHere;
            log?.AppendLine($"   굴곡선 '{nm}': 선 {blHere}개 · 구간 {nSeg - segBefore}개 → 교차 {hitHere}개" +
                            (nOutside - outBefore > 0 ? $" · 정지구간 밖이라 버린 것 {nOutside - outBefore}개" : ""));
        }

        int dup = Dedupe(list);
        log?.AppendLine($"  굴곡부 합계: 지표면 {nSurf}개 · 굴곡선 {nBl}개 · 구간 {nSeg}개 → " +
                        $"교차 {list.Count + dup}개(중복 {dup}개 합침) → {list.Count}개" +
                        (nOutside > 0 ? $" · 정지구간 밖 {nOutside}개" : "") +
                        (nSkipFar > 0 ? $" · 선형 밖 {nSkipFar}개" : "") +
                        (nTrim > 0 ? $" · 끝을 줄여 살린 선분 {nTrim}개" : ""));
        if (list.Count == 0)
            log?.AppendLine("  ⚠굴곡부가 하나도 안 나왔다 — 정지면에 굴곡선이 없거나 노선이 정지 범위를 안 지난다");
        return list;
    }

    /// <summary>★★[v25.3 · JACK 0811] <b>도면에 그려진 선과의 교차</b> — 데이라잇이 여기에 있다.
    ///
    /// <para>JACK: <i>"데이라잇선(계획지표면이 시작되는 지점)은 단면검토선이 안 끊어졌어."</i></para>
    ///
    /// <para><b>원인.</b> 가상 사면 지표면(<c>가상절토_DH</c>·<c>가상성토_DH</c>)은
    /// <b>오버사이즈</b>로 만든다 — 소단을 잘려나갈 몫까지 넉넉히 두르고 나중에 데이라잇으로 자른다.
    /// 그래서 그 굴곡선에는 <b>데이라잇이 없다</b>(실측: 두 면의 굴곡선 개수·구간 수가 9136으로
    /// 한 자리도 안 틀리고 같았다 — 같은 경계에서 같은 간격으로 두른 링이라는 뜻이다).
    /// 진짜 데이라잇은 <c>DrawDaylight</c>가 <b>레이어에 그려 둔다</b>.</para></summary>
    public static List<Mark> FromLayerLines(Transaction tr, Database db, CivilDb.Alignment al,
                                            IReadOnlyList<string> layers, string why,
                                            Func<double, bool> keep,
                                            System.Text.StringBuilder log)
    {
        var list = new List<Mark>();
        if (al == null || layers == null || layers.Count == 0) return list;
        double s0 = al.StartingStation, s1 = al.EndingStation;
        int nEnt = 0, nSeg = 0, nSkip = 0, nOutside = 0, nTrim = 0;
        try
        {
            var ms = (BlockTableRecord)tr.GetObject(SymbolUtilityServices.GetBlockModelSpaceId(db), OpenMode.ForRead);
            foreach (ObjectId id in ms)
            {
                Entity e;
                try { e = tr.GetObject(id, OpenMode.ForRead) as Entity; } catch { continue; }
                if (e == null) continue;
                bool want = false;
                foreach (var L in layers) if (string.Equals(e.Layer, L, StringComparison.Ordinal)) { want = true; break; }
                if (!want) continue;

                var pts = Vertices(e);
                if (pts.Count < 2) continue;
                nEnt++;
                Crossings(al, pts, why, s0, s1, keep, list, ref nSeg, ref nSkip, ref nOutside, ref nTrim);
            }
        }
        catch (System.Exception ex) { log?.AppendLine($"   도면선 읽기 실패 — {ex.Message}"); }

        int dup = Dedupe(list);
        log?.AppendLine($"   도면선 [{string.Join("·", layers)}]: 객체 {nEnt}개 · 구간 {nSeg}개 → " +
                        $"교차 {list.Count + dup}개(중복 {dup}개 합침) → {list.Count}개" +
                        (nOutside > 0 ? $" · 걸러진 것 {nOutside}개" : "") +
                        (nTrim > 0 ? $" · 끝을 줄여 살린 선분 {nTrim}개(기점·종점 언저리)" : ""));
        if (nEnt == 0) log?.AppendLine($"   ⚠레이어 [{string.Join("·", layers)}]에 선이 하나도 없다 — 부지정지를 먼저 돌려야 한다");
        return list;
    }

    /// <summary>★★[v30.2 · JACK 0812] <b>점렬 목록에서 바로 교차를 딴다 — 도면을 거치지 않는다.</b>
    /// <para>JACK: <i>"어느 순간엔 뭐 해야 하고 하는 식이면 제약이 생기고 범용성이 떨어져."</i>
    /// 그래서 사면선·소단선을 <b>도면에 그려져 있어야만</b> 읽을 수 있게 두지 않는다 —
    /// 번들에서 복원한 선을 그대로 넘겨받아 계산한다(<see cref="NoriCommand.RebuildEdgeLines"/>).</para></summary>
    public static List<Mark> FromLines(CivilDb.Alignment al,
                                       IReadOnlyList<List<Point3d>> lines, string why,
                                       Func<double, bool> keep,
                                       System.Text.StringBuilder log)
    {
        var list = new List<Mark>();
        if (al == null || lines == null || lines.Count == 0) return list;
        double s0 = al.StartingStation, s1 = al.EndingStation;
        int nSeg = 0, nSkip = 0, nOutside = 0, nTrim = 0;
        foreach (var pts in lines)
        {
            if (pts == null || pts.Count < 2) continue;
            Crossings(al, pts, why, s0, s1, keep, list, ref nSeg, ref nSkip, ref nOutside, ref nTrim);
        }
        int dup = Dedupe(list);
        log?.AppendLine($"   복원선 [{why}]: 선 {lines.Count}개 · 구간 {nSeg}개 → " +
                        $"교차 {list.Count + dup}개(중복 {dup}개 합침) → {list.Count}개" +
                        (nOutside > 0 ? $" · 걸러진 것 {nOutside}개" : "") +
                        (nTrim > 0 ? $" · 끝을 줄여 살린 선분 {nTrim}개(기점·종점 언저리)" : ""));
        return list;
    }

    /// <summary>★★[v30.4 · JACK 0812] <b>절성 경계 — 절토와 성토가 바뀌는 자리.</b>
    ///
    /// <para>JACK: <i>"절토와 성토 시점이 측점이 안 찍혔어."</i> — 맞다. 그 자리는
    /// <b>데이라잇도 소단도 사면도 아니다.</b> 부지 안쪽에서 원지반이 계획고를 가로지르는 자리라,
    /// 평면에 그려지는 어떤 선과도 겹치지 않는다. 그래서 <b>선과의 교차</b>로는 영영 안 잡힌다.</para>
    ///
    /// <para>대신 <b>두 종단의 표고차가 부호를 바꾸는 자리</b>다 — 계획면이 원지반 위(성토)에서
    /// 아래(절토)로 넘어가는 점. 그건 종단에서 직접 잰다.</para>
    ///
    /// <para><b>데이라잇과 헷갈리면 안 된다.</b> 정지 범위 <b>밖</b>에서는 두 선이 겹쳐 있어
    /// 표고차가 0 근처를 계속 떠다닌다 — 그걸 다 잡으면 노선 밖이 측점으로 뒤덮인다.
    /// 그래서 <b>0인 구간이 짧을 때만</b>(=steep하게 가로지를 때만) 절성 경계로 본다.
    /// 0인 구간이 길면 그건 '정지가 끝났다가 다시 시작한 것'이고, 그 양끝은 데이라잇이 이미 잡는다.</para></summary>
    public const double CutFillZeroRunMax = 3.0;

    public static List<Mark> FromCutFillLine(CivilDb.Profile pad, CivilDb.Profile ground,
                                             double s0, double s1, double step,
                                             System.Text.StringBuilder log)
    {
        var list = new List<Mark>();
        if (pad == null || ground == null || s1 <= s0) return list;
        step = Math.Max(0.1, step);

        // ★★[v32.3 · JACK 0812] <b>'값이 없다'를 예외에 기대지 않는다 — 범위를 직접 본다.</b>
        //
        //   순수 정지면(§27)부터 계획 종단은 <b>정지 구간에만</b> 존재한다. 그 밖을 물었을 때
        //   <c>ElevationAt</c>이 어떻게 구는지는 문서에 없다 — <b>예외를 던지면</b> 종전처럼 0으로 삼켜져
        //   건너뛰니 문제없지만, <b>0.0 같은 값을 돌려주면</b> 차이가 −110처럼 나와
        //   <b>정지 바깥 전체가 '성토'로 판정</b>되고 데이라잇 자리마다 가짜 절성경계가 하나씩 찍힌다.
        //
        //   → 묻기 <b>전에</b> 그 측점이 종단 범위 안인지 본다. 그러면 <c>ElevationAt</c>이 어떻게 굴든 상관없다.
        //   ※ 종단 중간이 비는 경우(구역이 떨어져 있을 때)는 범위로 못 거르므로 예외 갈래를 함께 남겨 둔다.
        static bool ElevAt(CivilDb.Profile p, double s, out double z)
        {
            z = 0;
            try
            {
                if (s < p.StartingStation - 1e-6 || s > p.EndingStation + 1e-6) return false;
                z = p.ElevationAt(s);
                return true;
            }
            catch { return false; }
        }

        int nNoData = 0;
        bool DiffAt(double s, out double d)
        {
            d = 0;
            if (!ElevAt(pad, s, out double zp) || !ElevAt(ground, s, out double zg)) { nNoData++; return false; }
            d = zp - zg;
            return true;
        }

        int Sign(double s)
            => DiffAt(s, out double d) ? (d > PadGroundTol ? 1 : d < -PadGroundTol ? -1 : 0) : 0;

        double Diff(double s) => DiffAt(s, out double d) ? d : 0;

        int prevSign = 0; double prevSignAt = s0;   // 마지막으로 ±였던 자리
        int nLong = 0;
        for (double s = s0; s <= s1 + 1e-9; s += step)
        {
            double t = Math.Min(s, s1);
            int g = Sign(t);
            if (g == 0) continue;                       // 겹침 구간 — 아직 판정 보류
            if (prevSign != 0 && g != prevSign)
            {
                if (t - prevSignAt <= CutFillZeroRunMax)
                {
                    // 부호가 바뀌었고 그 사이가 짧다 → 진짜 절성 경계. 표고차 0을 이분법으로 좁힌다.
                    double a = prevSignAt, b = t;
                    double da = Diff(a);
                    for (int it = 0; it < 40 && b - a > 1e-4; it++)
                    {
                        double m = (a + b) / 2.0;
                        if (Diff(m) * da > 0) a = m; else b = m;
                    }
                    list.Add(new Mark((a + b) / 2.0, "절성경계"));
                }
                else nLong++;                            // 사이가 길다 = 정지가 끊겼다 돌아온 것(데이라잇이 담당)
            }
            prevSign = g; prevSignAt = t;
        }

        int dup = Dedupe(list);
        log?.AppendLine($"   절성경계: {list.Count}개(절토↔성토가 바뀌는 자리)" +
                        (dup > 0 ? $" · 중복 {dup}개 합침" : "") +
                        (nLong > 0 ? $" · 겹침이 {CutFillZeroRunMax:0.#}m보다 길어 데이라잇에 맡긴 것 {nLong}곳" : "") +
                        // ★[v32.3 계측] 계획 종단이 없는 표본 수 — 순수 정지면이면 정지 <b>바깥</b> 몫이라 있는 게 정상이다.
                        //   0이면 계획 종단이 노선 전체를 덮고 있다는 뜻(합성면을 보고 있을 수 있다).
                        $" · 계획 종단 없는 표본 {nNoData}개");
        return list;
    }

    /// <summary>선·폴리선의 정점을 뽑는다(2D 판정용이라 Z는 안 쓴다). 모르는 종류는 빈 목록.</summary>
    private static List<Point3d> Vertices(Entity e)
    {
        var pts = new List<Point3d>();
        try
        {
            switch (e)
            {
                case Line ln: pts.Add(ln.StartPoint); pts.Add(ln.EndPoint); break;
                case Polyline pl:
                    for (int i = 0; i < pl.NumberOfVertices; i++) pts.Add(pl.GetPoint3dAt(i));
                    if (pl.Closed && pts.Count > 1) pts.Add(pts[0]);      // 닫힌 고리는 마지막 구간도 봐야 한다
                    break;
                case Polyline3d p3:
                    foreach (ObjectId vid in p3)
                        if (p3.Database.TransactionManager.TopTransaction?.GetObject(vid, OpenMode.ForRead)
                            is PolylineVertex3d v) pts.Add(v.Position);
                    if (p3.Closed && pts.Count > 1) pts.Add(pts[0]);
                    break;
                case Polyline2d p2:
                    foreach (ObjectId vid in p2)
                        if (p2.Database.TransactionManager.TopTransaction?.GetObject(vid, OpenMode.ForRead)
                            is Vertex2d v2) pts.Add(v2.Position);
                    if (p2.Closed && pts.Count > 1) pts.Add(pts[0]);
                    break;
            }
        }
        catch { }
        return pts;
    }

    // ── 합치는 쪽 ────────────────────────────────────────────────────────────

    /// <summary>정체인(일정 간격)과 특수측점을 합쳐 <b>최종 측점 목록</b>을 만든다.
    /// <para>특수측점이 우선이다 — 정체인이 <see cref="MergeTol"/> 안으로 붙으면 정체인을 버린다.
    /// 라벨이 겹쳐 못 읽는 도면보다 'No.5' 하나가 낫다.</para></summary>
    public static List<Mark> Merge(double stStart, double stEnd, double interval,
                                   IEnumerable<Mark> special, double tol = MergeTol)
    {
        // ① 특수측점 먼저(같은 자리 중복 제거 — 꺾임점과 구배변화점이 겹칠 수 있다)
        var outp = new List<Mark>();
        foreach (var m in special.Where(m => m.Station >= stStart - 1e-6 && m.Station <= stEnd + 1e-6)
                                 .OrderBy(m => m.Station))
        {
            int hit = outp.FindIndex(x => Math.Abs(x.Station - m.Station) <= tol);
            if (hit < 0) outp.Add(m);
            else if (!outp[hit].Why.Contains(m.Why))                     // 사유는 합쳐 둔다
                outp[hit] = outp[hit] with { Why = outp[hit].Why + "·" + m.Why };
        }
        // ② 정체인 — 특수측점에 가리지 않는 것만
        if (interval > 1e-6)
        {
            for (double s = stStart; s <= stEnd + 1e-9; s += interval)
            {
                double st = Math.Min(Math.Max(s, stStart), stEnd);
                if (outp.Any(x => Math.Abs(x.Station - st) <= tol)) continue;
                outp.Add(new Mark(st, "정체인"));
            }
            // 끝단이 정체인에서 멀면 하나 더(횡단 끝이 잘리지 않게)
            if (!outp.Any(x => Math.Abs(x.Station - stEnd) <= tol)) outp.Add(new Mark(stEnd, "종점"));
            if (!outp.Any(x => Math.Abs(x.Station - stStart) <= tol)) outp.Add(new Mark(stStart, "기점"));
        }
        return outp.OrderBy(m => m.Station).ToList();
    }

    /// <summary>측점을 'No.5+12.34' 꼴로 — 한국 종단도 관례.
    /// <para>★[v32.1 · JACK 0812] <b>'+' 뒤는 <c>00.00</c> — 두 자리로 채운다.</b>
    /// JACK: <i>"+00.00 형태로 바꾸고."</i> <c>0.00</c>이면 <c>+6.41</c>처럼 한 자리로 나와
    /// <c>+16.41</c>과 자릿수가 안 맞는다 — 측점 목록을 세로로 훑을 때 <b>자리가 흔들려 못 읽는다</b>.
    /// 색인이 20m라 나머지는 최대 19.99이므로 <b>두 자리면 넘치지 않는다</b>.</para></summary>
    public static string Fmt(double station, double index = 20.0)
    {
        if (index <= 1e-6) return station.ToString("0.00");
        int no = (int)Math.Floor(station / index + 1e-9);
        double plus = station - no * index;
        return plus < 1e-4 ? $"No.{no}" : $"No.{no}+{plus:00.00}";
    }
}
