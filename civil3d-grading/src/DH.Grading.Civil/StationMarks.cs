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

    /// <summary>측점 하나. <paramref name="Why"/>는 사람이 읽을 사유(밸브실·이형관·구배변화 등).
    /// <para>★[JACK 0825] <paramref name="Z"/>=그 자리 선의 <b>표고</b>. 종단에 옹벽·가시설을
    /// <b>굵은 수직 막대</b>로 그리려면 위·아래 표고가 있어야 한다. 기본값 <c>NaN</c>이라
    /// 표고를 안 채우는 기존 수집기는 손댈 것이 없다 — <b>모르는 것과 0은 다르다.</b></para></summary>
    /// <para>★[JACK 0825] <paramref name="X"/>·<paramref name="Y"/>=넘은 자리의 평면 좌표.
    /// 데이라잇에 잘려 <b>짝을 잃은 벽</b>은 반대편 선이 그 자리에서 선형을 안 넘는다 —
    /// 그때 반대편 선에서 <b>이 좌표에 가장 가까운 점</b>의 표고를 가져와 막대를 세운다.</para></summary>
    public readonly record struct Mark(double Station, string Why, double Z = double.NaN,
                                       double X = double.NaN, double Y = double.NaN);

    /// <summary>★[JACK 0825] 종단에 세울 <b>수직 막대</b> 하나 — 옹벽·가시설.
    /// <para>도면 관행: 옹벽은 직각 한 줄로 그린다. 스샷의 <b>시안 굵은 막대</b>(옹벽)와
    /// <b>마젠타 굵은 막대</b>(가시설)가 그것이다.</para></summary>
    /// <param name="Width">막대 폭(m) = <b>구배 × 벽 높이</b>.
    /// ★[JACK 0825] <i>"막대 굵기는 정하지 말고, 0.05로 쳐지니까 단높이를 가지고 폭을 계산하면 되잖아."</i>
    /// 맞다 — 그게 그 벽의 <b>진짜 두께</b>다. 임의의 값을 박으면 벽이 높든 낮든 같은 굵기로 그려진다.</param>
    /// <param name="Slope">그 벽의 구배 n(옹벽·가시설 모두 보통 0.05). 폭 = <c>n × 벽 높이</c>.
    /// 높이는 <b>종단에서 읽는 쪽이 정본</b>이므로 폭도 거기서 다시 계산한다 —
    /// 여기 <paramref name="ZTop"/>·<paramref name="ZBottom"/>은 선 교차에서 온 <b>물러설 값</b>이다.</param>
    public readonly record struct VertBar(double Station, double ZTop, double ZBottom,
                                          string Kind, double Slope);

    /// <summary>★★[JACK 0825] <b>벽 하나가 차지하는 앞·뒤 자리</b> — 횡단면도의 (전)(후)가 여기서 나온다.
    ///
    /// <para>JACK: <i>"보통 옹벽과 가시설은 같은 측점의 (전)(후)로 횡단면도를 생성해.
    /// 측점명은 같지만 (전)(후)라는 이름으로 두 개의 횡단면이 나와야 하고
    /// 한쪽엔 옹벽이 있고 한쪽엔 없는 게 만들어져야 해. 그게 일반적인 2D 횡단면도야."</i></para>
    ///
    /// <para>선형이 옹벽을 <b>가로지르면</b> 그 자리에서 지표면이 벽 높이만큼 뚝 떨어진다.
    /// 단면 하나로는 낮은 쪽·높은 쪽 중 하나만 담긴다 — 그래서 <b>두 장</b>이 필요하다.</para>
    ///
    /// <para><paramref name="Mid"/>는 <b>종단</b>이 쓰는 가운데(옹벽은 직각 한 줄),
    /// <paramref name="Front"/>·<paramref name="Back"/>은 <b>횡단</b>이 쓰는 벽 앞·뒤다.
    /// 접기 전의 크레스트·토우 자리가 그대로 이 둘이다 — 새로 계산하지 않는다.</para></summary>
    public readonly record struct WallSpan(double Mid, double Front, double Back, string Kind);

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

        // ★[JACK 0825] Z도 함께 보간한다 — 종전엔 0으로 버렸다.
        //   측점만 쓸 때는 무해했지만 표고를 쓰기 시작하면 <b>줄인 끝에서 0m가 튀어나온다</b>.
        static Point3d Along(Point3d a, Point3d b, double t)
            => new(a.X + (b.X - a.X) * t, a.Y + (b.Y - a.Y) * t, a.Z + (b.Z - a.Z) * t);

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

            double lo = 0.0, hi = 1.0, sHit = double.NaN, tHit = double.NaN;
            for (int it = 0; it < 40; it++)
            {
                double t = (lo + hi) / 2.0;
                var P = new Point3d(A.X + (B.X - A.X) * t, A.Y + (B.Y - A.Y) * t,
                                    A.Z + (B.Z - A.Z) * t);
                if (!Probe(P, out double stM, out double oM)) break;
                sHit = stM; tHit = t;
                if (oM == 0.0) break;
                if (oA * oM > 0) lo = t; else hi = t;
            }
            if (double.IsNaN(sHit)) continue;
            if (sHit < s0 - 1e-6 || sHit > s1 + 1e-6) { nSkip++; continue; }   // 선형 밖으로 연장된 자리
            if (keep != null && !keep(sHit)) { nOutside++; continue; }
            // ★[JACK 0825] 넘은 자리의 표고 — 그 선분 위에서 선형 보간. 종단 막대의 위·아래가 여기서 나온다.
            double zHit = double.IsNaN(tHit) ? double.NaN : A.Z + (B.Z - A.Z) * tHit;
            double xHit = double.IsNaN(tHit) ? double.NaN : A.X + (B.X - A.X) * tHit;
            double yHit = double.IsNaN(tHit) ? double.NaN : A.Y + (B.Y - A.Y) * tHit;
            outp.Add(new Mark(sHit, why, zHit, xHit, yHit)); hit++;
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

    /// <summary>★★[JACK 0825] <b>수직 벽은 두 줄로 서지만 측점은 하나다.</b>
    ///
    /// <para>JACK: <i>"실제 종단이나 횡단도면에서는 옹벽은 직각으로 표현하기 때문에 측점이 하나만 나와.
    /// 그런데 우린 꺾인 선이 두 개다 보니까 다 잡히네."</i> — 맞다.
    /// 옹벽(과 터파기 가시설)은 <b>윗선·아랫선 두 줄</b>로 만들어진다. 두 줄이면 선형을 넘는 자리도
    /// 둘이라 측점이 둘 선다. 도면 관행은 <b>직각 한 줄</b>이다.</para>
    ///
    /// <para><b>구배를 낮추는 것으로는 못 푼다.</b> 두 줄이 벌어지는 거리는 <c>구배 × 벽 높이</c>인데,
    /// 1:0.05에 5m 벽이면 25cm — <see cref="MergeTol"/>(1cm)의 스물다섯 배다. 1:0.01로 낮춰도
    /// 5cm라 여전히 안 합쳐지고, 합쳐질 만큼(1:0.002) 낮추면 <b>Civil 3D TIN이 깨진다</b>
    /// (구배 하한 0.05를 세운 바로 그 이유다). 게다가 두 줄은 3D 옹벽 매스·판넬·InfraWorks가
    /// 먹고 사는 재료라 얇게 만들면 그쪽이 같이 무너진다.</para>
    ///
    /// <para>그래서 <b>측점 쪽에서 접는다</b> — 같은 벽의 윗선·아랫선이 낸 두 측점을 <b>가운데 하나</b>로.
    /// <b>전역 <see cref="MergeTol"/>을 키우면 안 된다</b>: 0.5m 시절에 <c>No.1</c> 정측점을
    /// 먹었던 그 사고가 돌아온다. 짝을 아는 선끼리만 접는다.</para>
    ///
    /// <para><b>소단은 안 건드린다.</b> 짝은 <paramref name="walls"/>의 키로만 짓는데 그 키에 단 번호가
    /// 들어 있다 — 소단은 단이 달라 애초에 같은 조에 들어오지 않는다. 소단은 실제로 평면 폭이 있으니
    /// 측점이 따로 서는 것이 맞다.</para>
    ///
    /// <para>짝을 못 찾은 선은 <b>그대로 남긴다</b>. JACK 원칙 — <i>겹쳐 보이는 것보다 빠지는 것이 나쁘다.</i></para></summary>
    /// <param name="pairMax">짝으로 인정할 두 측점의 최대 거리(m). 벌어짐은 <c>구배×단높이</c>(≤0.05×15=0.75m)에
    /// 선형이 벽을 비스듬히 지나는 몫이 곱해진다. 이보다 멀면 짝이 아니라고 보고 둘 다 남긴다.</param>
    public static List<Mark> FromWallPairs<TKey>(
        CivilDb.Alignment al,
        IReadOnlyList<(TKey Key, bool IsCrest, List<Point3d> Pts, double Slope)> walls,
        string why, Func<double, bool> keep,
        System.Text.StringBuilder log, double pairMax = 3.0,
        List<VertBar> barsOut = null, List<WallSpan> spansOut = null)
    {
        var list = new List<Mark>();
        if (al == null || walls == null || walls.Count == 0) return list;
        double s0 = al.StartingStation, s1 = al.EndingStation;
        int nSeg = 0, nSkip = 0, nOutside = 0, nTrim = 0;
        int nPair = 0, nLone = 0, nWall = 0, nLoneBar = 0, nLoneMid = 0;
        // ★[JACK 0825] 자리를 찍는다 — '측점이 두 개 보인다'가 같은 벽인지 부지 반대편인지 갈린다.
        var pair = log != null ? new System.Text.StringBuilder() : null;
        var lone = log != null ? new System.Text.StringBuilder() : null;

        foreach (var g in walls.GroupBy(w => w.Key))
        {
            var crest = new List<Mark>();
            var toe = new List<Mark>();
            var crestPts = new List<List<Point3d>>();
            var toePts = new List<List<Point3d>>();
            double slope = 0.0;
            foreach (var w in g)
            {
                if (w.Pts == null || w.Pts.Count < 2) continue;
                if (w.Slope > slope) slope = w.Slope;      // 한 벽이면 같은 값이다
                (w.IsCrest ? crestPts : toePts).Add(w.Pts);
                Crossings(al, w.Pts, why, s0, s1, keep, w.IsCrest ? crest : toe,
                          ref nSeg, ref nSkip, ref nOutside, ref nTrim);
            }
            if (crest.Count == 0 && toe.Count == 0) continue;
            nWall++;

            // ★★[JACK 0825] <b>구배는 형상을 만들 때 쓴 값과 같아야 한다.</b>
            //   굴착 구배로 <b>0(수직)</b>을 넣으면 번들엔 0이 그대로 남는데, 실제 형상은
            //   <c>max(0, MinSlope)</c>=0.05로 만들어진다. 그 0을 폭 계산에 쓰면
            //   <b>폭 0 = 보이지 않는 막대</b>가 된다(실측: 가시설 폭 0m).
            double wSlope = Math.Max(slope, GradingSettings.MinSlope);

            // 가까운 것끼리 짝짓는다 — 한 벽이 조각으로 쪼개졌을 수 있다(구간 경계·클립).
            crest.Sort((a, b) => a.Station.CompareTo(b.Station));
            toe.Sort((a, b) => a.Station.CompareTo(b.Station));
            var taken = new bool[toe.Count];
            foreach (var c in crest)
            {
                int best = -1; double bd = double.MaxValue;
                for (int i = 0; i < toe.Count; i++)
                {
                    if (taken[i]) continue;
                    double d = Math.Abs(toe[i].Station - c.Station);
                    if (d < bd) { bd = d; best = i; }
                }
                if (best >= 0 && bd <= pairMax)
                {
                    taken[best] = true;
                    double mid = (c.Station + toe[best].Station) / 2.0;
                    pair?.Append($" [{mid:F2}m 벌어짐 {bd:F2}m]");
                    // 횡단이 쓸 앞·뒤 — 접기 전 두 자리를 그대로 남긴다(노선 진행 방향으로 앞이 작다).
                    spansOut?.Add(new WallSpan(mid,
                        Math.Min(c.Station, toe[best].Station),
                        Math.Max(c.Station, toe[best].Station), why));
                    double za = c.Z, zb = toe[best].Z;
                    list.Add(new Mark(mid, why, Math.Max(za, zb)));                   // ← 가운데 하나
                    // 종단 막대 — 위·아래는 <b>큰 쪽/작은 쪽</b>으로 잡는다. 절토·성토에 따라
                    // 크레스트가 아래로 오는 경우가 있어 이름만 믿으면 막대가 뒤집힌다.
                    if (barsOut != null && !double.IsNaN(za) && !double.IsNaN(zb))
                    {
                        double zHi = Math.Max(za, zb), zLo = Math.Min(za, zb);
                        barsOut.Add(new VertBar(mid, zHi, zLo, why, wSlope));
                    }
                    nPair++;
                }
                else
                {
                    nLone++;
                    bool ok = Lone(c, toePts, out double mid2); if (ok) nLoneBar++;
                    double put = double.IsNaN(mid2) ? c.Station : mid2;
                    list.Add(new Mark(put, why, c.Z, c.X, c.Y));
                    if (!double.IsNaN(mid2)) nLoneMid++;
                    lone?.Append($" [윗선 {c.Station:F2}m{(ok ? " 반대편찾음" : " 반대편없음(제자리에서 종단으로)")}" +
                                 $"{(double.IsNaN(mid2) ? "" : $" → 가운데 {mid2:F2}m")}]");
                }
            }
            for (int i = 0; i < toe.Count; i++)
                if (!taken[i])
                {
                    nLone++;
                    bool ok = Lone(toe[i], crestPts, out double mid3); if (ok) nLoneBar++;
                    double put = double.IsNaN(mid3) ? toe[i].Station : mid3;
                    list.Add(new Mark(put, why, toe[i].Z, toe[i].X, toe[i].Y));
                    if (!double.IsNaN(mid3)) nLoneMid++;
                    lone?.Append($" [아랫선 {toe[i].Station:F2}m{(ok ? " 반대편찾음" : " 반대편없음(제자리에서 종단으로)")}" +
                                 $"{(double.IsNaN(mid3) ? "" : $" → 가운데 {mid3:F2}m")}]");
                }

            // ★★[JACK 0825] <b>짝을 잃은 벽에도 막대를 세운다.</b>
            //
            //   JACK: <i>"막대가 온전한 한 단일 때만 생겨. 데이라잇에 잘린 옹벽은 안 생겼어."</i>
            //
            //   <b>원인.</b> 데이라잇이 벽의 <b>한쪽 선만</b> 자르면 그쪽은 선형을 안 넘는다.
            //   짝이 없으니 <c>nLone</c>으로 빠지고, 막대는 <b>짝지은 것만</b> 만들고 있었다.
            //   그런데 <b>벽은 거기 실제로 서 있다</b> — 선이 잘린 것이지 벽이 없는 게 아니다.
            //
            //   → 반대편 선에서 <b>그 자리에 가장 가까운 점</b>의 표고를 가져와 막대를 세운다.
            //     측점은 <b>옮기지 않는다</b> — 반대편 교차가 없으니 가운데를 잡을 근거가 없다.
            // ★★[JACK 0825 '계획지표면은 측점이 옹벽 윗선으로 되어 있어'] <b>자리까지 빌려 온다.</b>
            //   짝이 없다는 건 반대편 선이 그 자리에서 선형을 <b>안 넘는다</b>는 뜻이지,
            //   <b>그 자리에 벽이 없다</b>는 뜻이 아니다. 반대편 선에서 가장 가까운 점을 이미 찾고 있으므로
            //   그 점과의 <b>중점</b>이 곧 벽의 가운데다 — 표고만 빌려 오던 것을 자리까지 빌려 온다.
            //   <paramref name="midSt"/>가 NaN이 아니면 호출부가 그 측점으로 옮긴다.
            bool Lone(Mark m, List<List<Point3d>> others, out double midSt)
            {
                midSt = double.NaN;
                if (barsOut == null) return false;
                if (double.IsNaN(m.Z) || double.IsNaN(m.X) || double.IsNaN(m.Y)) return false;
                double best = double.MaxValue, zo = double.NaN, ox = double.NaN, oy = double.NaN;
                foreach (var L in others)
                    foreach (var pt in L)
                    {
                        double dx = pt.X - m.X, dy = pt.Y - m.Y, d2 = dx * dx + dy * dy;
                        if (d2 < best) { best = d2; zo = pt.Z; ox = pt.X; oy = pt.Y; }
                    }
                // 반대편 점과의 중점이 벽의 가운데다. 선형 범위를 벗어나면 그냥 제자리에 둔다.
                if (!double.IsNaN(ox))
                {
                    try
                    {
                        double st2 = 0, off2 = 0;
                        al.StationOffset((m.X + ox) / 2.0, (m.Y + oy) / 2.0, ref st2, ref off2);
                        if (st2 >= s0 - 1e-6 && st2 <= s1 + 1e-6 && (keep == null || keep(st2)))
                        {
                            midSt = st2;
                            // 반대편 점 자체의 측점도 재 둔다 — 그 둘이 벽의 앞·뒤다.
                            try
                            {
                                double stO = 0, offO = 0;
                                al.StationOffset(ox, oy, ref stO, ref offO);
                                if (stO >= s0 - 1e-6 && stO <= s1 + 1e-6)
                                    spansOut?.Add(new WallSpan(st2,
                                        Math.Min(m.Station, stO), Math.Max(m.Station, stO), why));
                            }
                            catch { }
                        }
                    }
                    catch { }
                }
                // ★★[JACK 0825] 반대편 선을 못 찾아도 <b>자리는 내보낸다.</b>
                //   데이라잇이 한쪽 선을 통째로 자르면 그 키에 조각이 하나도 안 남는다 —
                //   <b>드문 일이 아니라 정상 상황</b>이다: 절토 옹벽은 크레스트가, 성토 옹벽은 토우가
                //   원지반 쪽이라 잘려 나간다(실측: 벽 2개 모두 한쪽만 남아 짝짓기 0건).
                //
                //   그때는 <b>높이 0짜리 막대</b>가 되고, 측점도 못 옮긴다(<c>midSt</c>가 NaN).
                //   그러면 <c>DrawVertBars</c>가 <b>원래 자리에서 종단을 읽는다</b> —
                //   원래 자리는 크레스트든 토우든 <b>벽면 밖</b>이라 그게 정상 값이다.
                //   (가운데로 옮겼다면 벽면 한복판이라 중간 표고가 나왔을 것이다.)
                //
                //   <b>그러니 여기서 자리를 옮기지 않는 것이 안전장치다.</b> 나중에 "못 옮기는 걸 고치자"고
                //   손대면 그 자리가 벽면 한복판을 읽게 된다 — 고칠 거면 <c>WallSpan</c>도 함께 채워야 한다.
                double hi2, lo2;
                bool paired = !double.IsNaN(zo);
                if (!paired) { hi2 = m.Z; lo2 = m.Z; }
                else { hi2 = Math.Max(m.Z, zo); lo2 = Math.Min(m.Z, zo); }
                barsOut.Add(new VertBar(double.IsNaN(midSt) ? m.Station : midSt, hi2, lo2, why, wSlope));
                return paired;   // 반대편을 못 찾았으면 '세웠다'고 말하지 않는다 — 로그가 거짓이 된다
            }
        }

        int dup = Dedupe(list);
        log?.AppendLine($"   수직벽 [{why}]: 벽 {nWall}개 · 선 {walls.Count}개 · 구간 {nSeg}개 → " +
                        $"짝지어 가운데로 {nPair}개 · 짝 없어 그대로 {nLone}개" +
                        (nLone > 0 ? $"(반대편 선을 찾은 것 {nLoneBar}개 · 자리를 가운데로 옮긴 것 {nLoneMid}개" +
                                     $" · 나머지는 제자리에서 종단을 읽는다)" : "") +
                        (dup > 0 ? $" · 같은 자리 {dup}개 합침" : "") +
                        $" → {list.Count}개" +
                        (nOutside > 0 ? $" · 걸러진 것 {nOutside}개" : ""));
        if (pair != null && pair.Length > 0) log.AppendLine($"     짝지은 자리:{pair}");
        if (lone != null && lone.Length > 0) log.AppendLine($"     짝 없는 자리:{lone}");
        return list;
    }

    /// <summary>★★[JACK 0825] <b>터파기 측점 — 굴착 상단선과 구조물 바닥선.</b>
    ///
    /// <para>JACK: <i>"터파기부의 수직부는 옹벽이 아니라 가시설이니까 그것도 똑같이
    /// 측점은 가운데로 두고 두껍게 가는 걸로 하자."</i></para>
    ///
    /// <para>종전엔 <b>터파기 측점이 아예 안 잡혔다.</b> 측점 수집기가 정지 번들만 읽는데
    /// 터파기는 <b>별도 칸</b>(<c>EXCAV</c>)에 산다 — 그 칸을 아무도 안 열고 있었다.</para>
    ///
    /// <para><b>수직이냐 경사냐로 갈린다.</b>
    /// <list type="bullet">
    /// <item>굴착 구배가 <b>수직</b>(≤<see cref="GradingSettings.MinSlope"/>) = <b>가시설</b> —
    ///       상단·바닥이 사실상 같은 자리다. 도면 관행대로 <b>가운데 하나</b>로 접는다.</item>
    /// <item>굴착 구배가 <b>경사</b>(1:0.5 등) = 열린 터파기 — 상단과 바닥이 실제로 몇 미터 떨어져 있다.
    ///       <b>둘 다 세운다</b>(접으면 법면이 측점에서 사라진다).</item>
    /// </list></para>
    ///
    /// <para>바닥·상단은 <b>닫힌 링</b>이라 첫 점을 끝에 붙여 닫는다 —
    /// 안 닫으면 마지막 점과 첫 점 사이 한 변의 교차가 통째로 빠진다.</para></summary>
    public static List<Mark> FromExcavation(CivilDb.Alignment al, Database db, Transaction tr,
                                            Func<double, bool> keep, System.Text.StringBuilder log,
                                            List<VertBar> barsOut = null, List<WallSpan> spansOut = null)
    {
        var list = new List<Mark>();
        if (al == null || db == null || tr == null) return list;

        List<ExcavBundle> exs;
        try
        {
            var loaded = ExcavBundleStore.TryLoadAll(db, tr, out string why0);
            if (loaded == null || loaded.Count == 0) { log?.AppendLine($"   터파기: 없음({why0})"); return list; }
            exs = loaded;
        }
        catch (Exception ex) { log?.AppendLine("   터파기 기록 읽기 실패 — " + ex.Message); return list; }

        static List<Point3d> Closed(IReadOnlyList<Core.Point3> r)
        {
            var q = new List<Point3d>(r.Count + 1);
            foreach (var p in r) q.Add(new Point3d(p.X, p.Y, p.Z));
            if (q.Count >= 3 &&
                (Math.Abs(q[0].X - q[^1].X) > 1e-6 || Math.Abs(q[0].Y - q[^1].Y) > 1e-6)) q.Add(q[0]);
            return q;
        }

        var flat = new List<List<Point3d>>();                                  // 경사 터파기 — 둘 다
        var pairs = new List<((int Structure, int Bench) Key, bool IsCrest, List<Point3d> Pts,
                             double Slope)>();                                 // 가시설 — 가운데로
        int nVert = 0, nOpen = 0;
        for (int i = 0; i < exs.Count; i++)
        {
            var e = exs[i];
            if (e == null) continue;
            bool vertical = e.Slope <= GradingSettings.WallGateSlope + 1e-9;
            var bottom = (e.Bottom != null && e.Bottom.Count >= 3) ? Closed(e.Bottom) : null;
            var top = (e.FinalRing != null && e.FinalRing.Count >= 3) ? Closed(e.FinalRing) : null;
            if (vertical)
            {
                if (top != null) pairs.Add(((i, 0), true, top, System.Math.Max(e.Slope, e.MinSlope)));
                if (bottom != null) pairs.Add(((i, 0), false, bottom, System.Math.Max(e.Slope, e.MinSlope)));
                // ★[JACK 0825] 세션 전역이 아니라 <b>그 기록의 하한</b>을 쓴다 — 옛 터파기를 열어도 두께가 안 변한다.
                nVert++;
            }
            else
            {
                if (top != null) flat.Add(top);
                if (bottom != null) flat.Add(bottom);
                nOpen++;
            }
        }

        if (flat.Count > 0) list.AddRange(FromLines(al, flat, "터파기", keep, log));
        if (pairs.Count > 0) list.AddRange(FromWallPairs(al, pairs, "가시설", keep, log, 3.0, barsOut, spansOut));
        int dup = Dedupe(list);
        log?.AppendLine($"   터파기 합계: 구조물 {exs.Count}개(가시설 {nVert} · 열린굴착 {nOpen})" +
                        (dup > 0 ? $" · 같은 자리 {dup}개 합침" : "") + $" → 측점 {list.Count}개");
        return list;
    }

    /// <summary>★★[JACK 0825] <b>벽 두께 안에 든 데이라잇 측점을 벽 자리로 끌어당긴다.</b>
    ///
    /// <para>JACK: <i>"측점도 중심점 외에 옹벽 시점·종점에 측점이 생겨."</i></para>
    ///
    /// <para><b>원인.</b> 옹벽의 한쪽 선이 <b>곧 데이라잇</b>이다 — 벽 상단이 원지반과 만나는
    /// 그 자리가 데이라잇이다. 그래서 같은 벽에서 두 사유가 각각 측점을 세운다. 실측:
    /// <code>
    /// 14.28m 데이라잇   14.40m 옹벽    ← 12cm
    /// 47.20m 옹벽       47.30m 데이라잇 ← 10cm
    /// </code>
    /// 이 간격이 바로 <b>구배 × 벽 높이</b>, 곧 <b>벽 두께</b>다. 도면에서 옹벽은 직각 한 줄이니
    /// 그 두께 안은 <b>한 자리</b>로 봐야 한다.</para>
    ///
    /// <para><b>지우지 않고 옮긴다.</b> 데이라잇은 지형 정보라 없애면 안 된다 —
    /// 벽 측점과 <b>같은 자리</b>로 만들면 <see cref="Dedupe"/>가 하나로 합치고 사유는 남는다.
    /// 벽 두께 밖은 손대지 않으므로 정측점·원지반굴곡은 안전하다.</para></summary>
    public static int PullDaylightToWalls(List<Mark> list, IReadOnlyList<VertBar> bars,
                                          System.Text.StringBuilder log)
    {
        if (list == null || bars == null || bars.Count == 0) return 0;
        int moved = 0;
        var note = log != null ? new System.Text.StringBuilder() : null;
        foreach (var b in bars)
        {
            double h = b.ZTop - b.ZBottom;
            if (double.IsNaN(h) || h <= 0) continue;
            // ★★[JACK 0825 '측점이 미세하게 겹쳐졌다'] 벽 두께 <b>+ 한 칸</b>.
            //   종전엔 <c>max(두께, MergeTol)</c>이었다. 낮은 벽은 두께가 1cm 밑이라 tol이 MergeTol에
            //   갇히는데, 데이라잇이 <b>1.2cm쯤</b> 떨어져 있으면 간발의 차로 밖이 된다
            //   (실측: 42.08 벽은 못 당기고 42.09 데이라잇이 그대로 남았다).
            //   두께에 한 칸을 <b>더해</b> 그 경계를 없앤다 — 데이라잇만 당기므로 정측점은 안전하다.
            double tol = Math.Abs(b.Slope * h) + MergeTol;
            for (int i = 0; i < list.Count; i++)
            {
                var m = list[i];
                if (m.Why == null) continue;
                if (m.Why.IndexOf("데이라잇", StringComparison.Ordinal) < 0) continue;
                if (m.Why.IndexOf("옹벽", StringComparison.Ordinal) >= 0 ||
                    m.Why.IndexOf("가시설", StringComparison.Ordinal) >= 0) continue;   // 이미 벽 것
                double d = Math.Abs(m.Station - b.Station);
                // ★[JACK 0825] 하한 가드를 MergeTol(10mm) → 1e-9로. 벽 두께가 20mm쯤이면
                //   실제 거리(두께의 절반)가 10mm 이하로 떨어져 <b>당기지도 합치지도 않는</b> 창이 생겼다
                //   (Dedupe는 &lt;10mm 엄격 비교라 딱 그 값은 안 합친다). 당겨서 겹친 것은 아래 Dedupe가 합친다.
                if (d > tol || d <= 1e-9) continue;          // 두께 밖이거나 완전히 같은 점
                note?.Append($" [{m.Station:F2}→{b.Station:F2}m]");
                list[i] = new Mark(b.Station, m.Why, m.Z, m.X, m.Y);
                moved++;
            }
        }
        if (moved > 0)
        {
            int dup = Dedupe(list);
            log?.AppendLine($"     벽 두께 안 데이라잇 {moved}개를 벽 자리로 당겼다" +
                            (dup > 0 ? $"(합쳐서 {dup}개 줄었다)" : "") + note);
        }
        return moved;
    }

    /// <summary>★[JACK 0825] 종단에 세울 <b>수직 막대</b>를 전부 모은다 — 옹벽 + 터파기 가시설.
    /// <para>측점과 <b>같은 자</b>를 쓴다(같은 복원·같은 짝짓기). 그래야 막대가 선 자리에
    /// 측점이 정확히 서고, 도면에서 둘이 어긋나 보이지 않는다.</para></summary>
    public static List<VertBar> CollectVertBars(CivilDb.Alignment al, Database db, Transaction tr,
                                                System.Text.StringBuilder log)
    {
        var bars = new List<VertBar>();
        if (al == null || db == null || tr == null) return bars;

        // ① 옹벽 — 정지 번들에서 복원한다(도면에 그려져 있든 말든).
        try
        {
            var regions = GradingBundleStore.TryLoadAll(db, tr, out _);
            if (regions != null && regions.Count > 0)
            {
                var walls = new List<((int Region, bool Up, int Ring, int Bench) Key, bool IsCrest,
                                      List<Core.Point3> Pts, double Slope)>();
                Commands.NoriCommand.RebuildEdgeLines(regions, out _, walls);
                var wpts = new List<((int Region, bool Up, int Ring, int Bench) Key, bool IsCrest,
                                     List<Point3d> Pts, double Slope)>(walls.Count);
                foreach (var w in walls)
                {
                    var q = new List<Point3d>(w.Pts.Count);
                    foreach (var pt in w.Pts) q.Add(new Point3d(pt.X, pt.Y, pt.Z));
                    wpts.Add((w.Key, w.IsCrest, q, w.Slope));
                }
                if (wpts.Count > 0) FromWallPairs(al, wpts, "옹벽", null, log, 3.0, bars);
            }
        }
        catch (Exception ex) { log?.AppendLine("   종단 옹벽 막대 실패 — " + ex.Message); }

        // ② 가시설 — 터파기 번들. 수직 굴착만 막대가 된다(경사 터파기는 법면이라 막대가 아니다).
        try { FromExcavation(al, db, tr, null, log, bars); }
        catch (Exception ex) { log?.AppendLine("   종단 가시설 막대 실패 — " + ex.Message); }

        return bars;
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

    /// <summary>원지반 꺾은선의 한 점 — 측점과 표고. <see cref="SimplifyGround"/>가 돌려준다.</summary>
    public readonly record struct GroundPt(double Station, double Elev);

    /// <summary>★★[v32.23 · JACK 0812] <b>꺾은선의 꺾임점이 곧 측점이다.</b>
    /// <para>JACK: <i>"꺾은선으로 바꿨으면 조금이라도 종단상에서 각진 부분은 측점으로 추가해야 해."</i>
    /// <see cref="SimplifyGround"/>가 돌려준 <b>그 목록 하나</b>로 꺾은선도 그리고 측점도 잡는다 —
    /// <b>두 번 계산하면 반드시 어긋난다</b>(이 저장소가 §20·§26에서 되풀이해 배운 것).</para>
    /// <para>양 끝만 뺀다 — 그건 꺾임이 아니라 노선의 끄트머리이고 <see cref="Merge"/>가 기점·종점으로 넣는다.</para></summary>
    public static List<Mark> MarksFromGround(IReadOnlyList<GroundPt> pts)
    {
        var list = new List<Mark>();
        if (pts == null || pts.Count < 3) return list;
        for (int i = 1; i < pts.Count - 1; i++) list.Add(new Mark(pts[i].Station, "원지반굴곡"));
        return list;
    }

    /// <summary>★★[v32.21 · JACK 0812] <b>원지반의 수직 굴곡부 — 토공량 때문에 넣는다.</b>
    ///
    /// <para>JACK: <i>"원지반의 수직굴곡부도 전부 추가해야해. 그래야 나중에 횡단에서 토공을 구할수있어(2d도면납품용).
    /// 그런데 지금 계획지표면부분만 가져오다 보니…"</i> — 맞다. 지금 측점의 출처는 <b>전부 계획면 쪽</b>이다
    /// (절성경계·데이라잇·사면·소단). 원지반이 혼자 꺾이는 자리는 한 곳도 안 들어간다.</para>
    ///
    /// <para><b>왜 문제인가.</b> 2D 납품 토공은 <b>평균단면법</b>이다 — 이웃한 두 횡단의 면적을 평균해
    /// 그 사이 거리를 곱한다. 이는 <b>두 단면 사이에서 지반이 직선으로 변한다</b>고 가정하는 것이라,
    /// 그 사이에서 원지반이 꺾이면 <b>꺾인 만큼이 그대로 체적 오차</b>가 된다.</para>
    ///
    /// <para><b>그렇다고 전부 쓸 수는 없다.</b> 지표면에서 딴 종단은 TIN 삼각형 모서리를 넘을 때마다
    /// 점이 생겨 수백~수천 개다.</para>
    ///
    /// <para><b>그래서 '얼마나 틀려도 되는가'로 고른다</b>(<paramref name="tolZ"/>).
    /// 남긴 점들을 직선으로 이었을 때 <b>실제 원지반과의 수직 차이가 어디서도 <paramref name="tolZ"/>를
    /// 넘지 않도록</b> 고른다(Douglas-Peucker, 수직 편차 기준).</para>
    ///
    /// <para><b>§24에서 폐기한 '굴곡부 찾기'와 무엇이 다른가.</b> 그때는 계획면의 <b>설계 의도</b>를
    /// 추측하려 했고, 지표면 표본점과 설계 변화점이 <b>원리상 갈리지 않아</b> 폐기했다(62m에 78개).
    /// 원지반에는 의도가 없다 — 추측할 것이 없고, <b>허용 오차를 정하면 답이 유일하게 정해진다.</b>
    /// 같은 '문턱값'이지만 <b>재는 대상이 다르다</b>: 그때는 '설계인가?'(알 수 없다),
    /// 지금은 '토공이 얼마나 틀리는가?'(정확히 계산된다).</para>
    ///
    /// <para><b>수직 편차이지 점-선분 최단거리가 아니다.</b> 측점(m)과 표고(m)는 단위는 같아도
    /// 종단도는 수직을 과장해 그리고, 무엇보다 <b>토공 오차를 지배하는 것은 수직 차이</b>다.
    /// 최단거리로 재면 급경사에서 실제 높이오차가 <paramref name="tolZ"/>를 넘어간다.</para>
    ///
    /// <para><b>양 끝을 포함해</b> 돌려준다 — 이 목록은 선을 그리는 데도 쓰이고, 선에는 끝이 있어야 한다.
    /// 측점으로 쓸 때는 <see cref="MarksFromGround"/>가 양 끝을 뺀다(끄트머리는 꺾임이 아니고,
    /// <see cref="Merge"/>가 기점·종점으로 이미 넣는다 — <see cref="FromRouteVertices"/>와 같은 규칙).</para>
    ///
    /// <para><b>여기 자가검증이 재는 것은 이 단계의 결과뿐이다.</b> 도면에 실제로 그려지는 선은
    /// 여기에 <b>다른 측점들까지 더해</b> 다시 이은 것이라 편차가 달라진다(점을 더 넣는다고 줄지 않는다).
    /// 그 최종 검증은 선을 만드는 자리에서 따로 한다 — <c>ProfileCommand.RebuildGroundAsPolyline</c> ⑤.</para></summary>
    public static List<GroundPt> SimplifyGround(CivilDb.Profile ground, double s0, double s1,
                                                double tolZ, System.Text.StringBuilder log)
    {
        var list = new List<GroundPt>();
        if (ground == null || s1 <= s0) return list;
        tolZ = Math.Max(0.01, tolZ);

        // ── ① 표본점을 (측점, 표고)로 모은다.
        var raw = new List<(double S, double Z)>();
        int nOut = 0, nBad = 0;
        try
        {
            foreach (CivilDb.ProfilePVI q in ground.PVIs)
            {
                double s, z;
                // ★ <c>Station</c>이 아니라 <c>RawStation</c>이다 — 앞은 폐기 예정이고, 측점식(방정식)이
                //   걸린 선형에서 값이 달라진다. 여기 측점은 <b>단면검토선을 놓을 자리</b>라 생 측점이 맞다.
                try { s = q.RawStation; z = q.Elevation; }
                catch { nBad++; continue; }
                if (double.IsNaN(s) || double.IsNaN(z) ||
                    double.IsInfinity(s) || double.IsInfinity(z)) { nBad++; continue; }
                if (s < s0 - 1e-6 || s > s1 + 1e-6) { nOut++; continue; }
                raw.Add((s, z));
            }
        }
        catch (System.Exception ex)
        { log?.AppendLine("  원지반 굴곡부: PVI를 못 읽어 건너뜀 — " + ex.Message); return list; }

        raw.Sort((a, b) => a.S.CompareTo(b.S));

        // 같은 측점이 겹치면(수직 절벽) 하나만 남긴다 — 아래 보간에서 0으로 나누는 것을 막는다.
        var p = new List<(double S, double Z)>(raw.Count);
        foreach (var t in raw)
            if (p.Count == 0 || t.S - p[p.Count - 1].S > 1e-6) p.Add(t);

        int n = p.Count;
        if (n < 3)
        {
            log?.AppendLine($"  원지반 굴곡부: 표본점 {n}개 — 고를 것이 없다(꺾임을 재려면 3개는 있어야 한다)");
            return list;
        }

        // ── ② Douglas-Peucker(수직 편차) — 재귀 대신 스택. 표본점이 수천 개라 재귀는 깊이가 위험하다.
        var keep = new bool[n];
        keep[0] = true; keep[n - 1] = true;
        var stack = new Stack<(int A, int B)>();
        stack.Push((0, n - 1));
        while (stack.Count > 0)
        {
            var (a, b) = stack.Pop();
            if (b - a < 2) continue;                       // 사이에 점이 없다
            double sa = p[a].S, za = p[a].Z;
            double ds = p[b].S - sa, dz = p[b].Z - za;
            int worst = -1; double worstDev = tolZ;        // tolZ를 넘는 것만 후보
            for (int i = a + 1; i < b; i++)
            {
                double zLine = ds > 1e-9 ? za + dz * (p[i].S - sa) / ds : za;
                double dev = Math.Abs(p[i].Z - zLine);
                if (dev > worstDev) { worstDev = dev; worst = i; }
            }
            if (worst < 0) continue;                       // 이 구간은 직선으로 충분하다
            keep[worst] = true;
            stack.Push((a, worst));
            stack.Push((worst, b));
        }

        // ── ③ [자가검증] 남긴 점으로 이었을 때 <b>실제</b> 최대 수직 편차를 다시 잰다.
        //   이 저장소가 값비싸게 배운 규칙이다: <b>자를 먼저 의심하라.</b> DP를 믿지 말고 결과를 잰다.
        //   이 값이 tolZ를 넘으면 고르기가 잘못된 것이고, 로그가 그 자리에서 말해 준다.
        double maxDev = 0; double maxDevAt = 0;
        int prev = 0;
        for (int i = 1; i < n; i++)
        {
            if (!keep[i]) continue;
            double sa = p[prev].S, za = p[prev].Z;
            double ds = p[i].S - sa, dz = p[i].Z - za;
            for (int j = prev + 1; j < i; j++)
            {
                double zl = ds > 1e-9 ? za + dz * (p[j].S - sa) / ds : za;
                double d = Math.Abs(p[j].Z - zl);
                if (d > maxDev) { maxDev = d; maxDevAt = p[j].S; }
            }
            prev = i;
        }

        // ── ④ 남긴 점이 <b>꺾은선의 정점</b>이다 — 양 끝을 포함해 돌려준다(선을 그리려면 끝이 있어야 한다).
        //   측점은 여기서 양 끝만 뺀 것이고, 그 변환은 <see cref="MarksFromGround"/>가 한다.
        for (int i = 0; i < n; i++)
            if (keep[i]) list.Add(new GroundPt(p[i].S, p[i].Z));
        int nKeep = System.Math.Max(0, list.Count - 2);

        log?.AppendLine(
            $"  원지반 꺾은선 정점 {list.Count}개(측점이 되는 꺾임 {nKeep}개) — 표본점 {n}개에서 골랐다(허용 높이오차 {tolZ:0.###}m)"
            + (nOut > 0 ? $" · 노선 밖 {nOut}개 제외" : "")
            + (nBad > 0 ? $" · 못 읽음 {nBad}개" : "")
            + $"\n    이 단계 검증(고른 점만으로 이었을 때): 최대 높이오차 {maxDev:0.###}m"
            + $" @ {(maxDev > 1e-9 ? maxDevAt.ToString("0.00") + "m" : "-")}"
            + (maxDev <= tolZ + 1e-6 ? " → 허용치 안" : "  ⚠허용치를 넘었다 — 고르기가 잘못됐다")
            + "  ※도면에 그려질 선의 검증은 '원지반 꺾은선' 줄에 따로 찍힌다");

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
