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

    /// <summary>계획 종단의 <b>구배변화점</b>을 측점으로(수직 이형관 / 부지정지의 경사 변화).
    /// JACK 0810: "계획면 구배변화점은 측점 있어야 해."</summary>
    /// <summary>구배가 이만큼(5%p) 넘게 꺾여야 '구배변화점'으로 친다.
    /// <para>★[JACK 0810 실측] 지표면에서 뽑은 종단은 삼각망을 지날 때마다 미세하게 꺾인다 —
    /// 62m 노선에서 <b>78개</b>가 잡혔다(0.8m마다 하나). 그건 설계된 변화가 아니라 <b>표본 잡음</b>이다.
    /// 실제로 의미 있는 것은 소단·사면 경계처럼 구배가 확 바뀌는 자리뿐이다.</para></summary>
    public const double GradeBreakTol = 0.05;

    /// <summary>구배변화점끼리 이보다 가까우면 큰 쪽 하나만 남긴다 — 한 자리에 라벨이 겹치지 않게.</summary>
    public const double GradeBreakMinGap = 2.0;

    /// <summary>★[JACK 0810] <b>정지면과 원지반이 겹치는 구간은 공제한다.</b>
    ///
    /// <para>JACK: "계획지표면은 원지반과 합성되어 있으니깐 꼭 겹치는 구간 공제하는 로직이 있어야 해.
    /// 아니면 원래 원지반 구간인데도 표면 굴곡 때문에 체인 끊어질 수도 있어."</para>
    ///
    /// <para><b>정지면은 순수한 계획면이 아니라 합성면이다.</b> 정지 범위 밖에서는 원지반을 그대로
    /// 베껴 쓴다 — 그 구간의 꺾임은 <b>설계가 아니라 지형</b>이다. 실측에서 62m 노선에 78개가 잡힌
    /// 이유가 이것이다. 두 선의 표고 차가 이 값 이하이면 '겹친다'로 보고 그 구간의 꺾임을 버린다.</para>
    ///
    /// <para>값은 <b>실제 거리(m)</b>다 — 축척과 무관해야 한다. 종단·횡단이 같은 측점을 써야 하는데
    /// 축척에 따라 집합이 달라지면 두 도면이 어긋난다.</para></summary>
    public const double PadGroundTol = 0.05;

    /// <summary>계획 종단의 구배변화점. <paramref name="groundProfileId"/>를 주면
    /// <b>원지반과 겹치는 구간을 공제</b>하고, 갈라지거나 합쳐지는 <b>경계</b>를 측점으로 잡는다
    /// (사면 시·종점 = 데이라이트 자리라 도면에서 반드시 필요하다).</summary>
    public static List<Mark> FromProfileGradeBreaks(Transaction tr, ObjectId profileId,
                                                    ObjectId groundProfileId = default)
    {
        var list = new List<Mark>();
        if (profileId.IsNull) return list;
        try
        {
            if (tr.GetObject(profileId, OpenMode.ForRead) is not CivilDb.Profile pr) return list;
            double s0 = pr.StartingStation, s1 = pr.EndingStation;

            // ── 원지반 종단을 손에 쥔다. 없으면 공제 없이 종전대로 간다(기능이 죽지는 않게).
            CivilDb.Profile gr = null;
            if (!groundProfileId.IsNull)
                try { gr = tr.GetObject(groundProfileId, OpenMode.ForRead) as CivilDb.Profile; } catch { }

            /// 이 측점에서 두 선이 떨어져 있는가(=정지 구간인가).
            bool Graded(double s)
            {
                if (gr == null) return true;                 // 원지반을 모르면 전부 대상으로 둔다
                try { return Math.Abs(pr.ElevationAt(s) - gr.ElevationAt(s)) > PadGroundTol; }
                catch { return false; }                      // 범위 밖이면 대상 아님
            }

            // ① PVI를 측점 순으로 모은다
            var pts = new List<(double S, double E)>();
            foreach (CivilDb.ProfilePVI pvi in pr.PVIs)
            {
                try { pts.Add((pvi.Station, pvi.Elevation)); } catch { }
            }
            pts.Sort((a, b) => a.S.CompareTo(b.S));

            // ② 앞뒤 구배를 직접 재서 **꺾인 정도**로 거른다.
            //    Civil 3D는 표본점마다 PVI를 만들므로 '개수'로는 설계 변화와 잡음을 못 가른다.
            var cand = new List<(double S, double D)>();
            for (int i = 1; i < pts.Count - 1; i++)
            {
                double s = pts[i].S;
                if (s <= s0 + 1e-6 || s >= s1 - 1e-6) continue;          // 시작·끝은 꺾임이 아니다
                double dL = pts[i].S - pts[i - 1].S, dR = pts[i + 1].S - pts[i].S;
                if (dL < 1e-6 || dR < 1e-6) continue;
                double gIn = (pts[i].E - pts[i - 1].E) / dL;
                double gOut = (pts[i + 1].E - pts[i].E) / dR;
                double d = Math.Abs(gOut - gIn);
                // ★[JACK 0810] 겹치는 구간의 꺾임은 지형이지 설계가 아니다 — 버린다.
                if (d >= GradeBreakTol && Graded(s)) cand.Add((s, d));
            }

            // ②-b ★[JACK 0810] <b>갈라지고 합쳐지는 경계</b>를 잡는다 — 사면 시·종점(데이라이트).
            //     정지 구간의 시작과 끝이라 도면에서 가장 중요한 체인 중 하나다.
            //     PVI 사이에서 상태가 바뀌면 그 사이를 이분법으로 좁혀 자리를 찾는다.
            if (gr != null)
            {
                bool prev = Graded(pts.Count > 0 ? pts[0].S : s0);
                for (int i = 1; i < pts.Count; i++)
                {
                    bool cur = Graded(pts[i].S);
                    if (cur == prev) continue;
                    double a = pts[i - 1].S, b = pts[i].S;
                    for (int it = 0; it < 30 && b - a > 1e-3; it++)      // 1mm까지 좁힌다
                    {
                        double m = (a + b) / 2.0;
                        if (Graded(m) == prev) a = m; else b = m;
                    }
                    cand.Add(((a + b) / 2.0, double.MaxValue));         // 경계는 무조건 살린다
                    prev = cur;
                }
            }

            // ③ 붙어 있는 것끼리는 **가장 크게 꺾인 것 하나만** 남긴다
            //    (경계는 D=MaxValue라 정렬에서 맨 앞에 서므로 절대 밀려나지 않는다.)
            foreach (var c in cand.OrderByDescending(x => x.D))
            {
                if (list.Any(m => Math.Abs(m.Station - c.S) < GradeBreakMinGap)) continue;
                list.Add(new Mark(c.S, c.D == double.MaxValue ? "정지경계" : "구배변화"));
            }
            list = list.OrderBy(m => m.Station).ToList();
        }
        catch { }
        return list;
    }

    // ── 굴곡부 = 선형 × 정지면 굴곡선의 2D 교차 ──────────────────────────────

    /// <summary>★★[v25.0 · JACK 0811 확정] <b>굴곡부는 선을 만나는 자리다 — 표본점을 세는 게 아니라.</b>
    ///
    /// <para><b>왜 방식을 바꿨나.</b> 종전엔 계획 종단의 PVI를 훑어 '많이 꺾인 것'을 골랐다
    /// (<see cref="FromProfileGradeBreaks"/>). 그런데 지표면에서 딴 종단은 <b>삼각망을 지날 때마다</b>
    /// PVI가 생긴다 — 실측에서 평평한 구간(계획고 112.00)에 PVI가 20개 넘게 이어졌다.
    /// 그건 꺾인 자리가 아니라 <b>표본점</b>이라, 허용오차를 아무리 다듬어도 설계와 잡음을 못 가른다.</para>
    ///
    /// <para>JACK: <i>"굴곡부는 선형과 계획지표면의 소단 또는 옹벽선과의 2D 교차점 측점을 찾고
    /// 그걸 단면검토선에 추가하는 방식으로 가는 건 어때? 어차피 애드인의 결과물로 만들 종단이니깐."</i>
    /// <i>"정지면을 만드는 데 있어서 생성되는 모든 선이 들어가야 해 — 지표면과 닿는 데이라잇,
    /// 소단선, 사면선, 옹벽선 정도면 되지 않을까?"</i></para>
    ///
    /// <para><b>맞는 말이다.</b> 정지면은 우리가 만든 면이고, 그 면을 만든 <b>굴곡선(breakline)</b>이
    /// 곧 데이라잇·소단·사면·옹벽이다. 종단이 꺾이는 자리는 <b>선형이 그 선을 넘는 자리</b>다.
    /// 추정이 아니라 <b>계산</b>이고, 허용오차가 필요 없다.</para>
    ///
    /// <para><b>도면의 선이 아니라 지표면의 굴곡선에서 읽는다.</b> 도면에 그려지는
    /// <c>DH-소단선-*</c>·<c>DH-사면선-*</c>은 <b>표현용</b>이라 레이어가 개편되면 빠질 수 있고
    /// (실제로 구 <c>DH-소단</c>은 지금 비워진다), <c>DH-노리선</c>은 굴곡선이 아니라
    /// <b>해칭 tick(짧은 선 수십 개)</b>이라 교차시키면 엉뚱한 자리가 쏟아진다.
    /// 굴곡선은 <b>정지면의 실체</b>라 그런 흔들림이 없다.</para>
    ///
    /// <para><b>교차는 이분법으로 찾는다.</b> Civil의 선형은 AcDb 곡선이 아니라 <c>IntersectWith</c>가
    /// 없다. 대신 <c>StationOffset</c>이 주는 <b>부호 있는 이격</b>을 쓴다 — 굴곡선 한 구간의
    /// 양 끝에서 부호가 바뀌면 그 사이에서 선형을 넘은 것이다. 곡선 선형에도 그대로 통한다.</para>
    ///
    /// <para><b>솎지 않는다</b>(JACK: "최소간격 없어 둘 다 찍어"). 소단을 비스듬히 지나 30cm 간격으로
    /// 둘이 나와도 둘 다 남긴다 — 겹쳐 보이는 것보다 빠지는 게 나쁘다.</para></summary>
    /// <param name="surfIds">굴곡선을 읽을 지표면들. <b>원지반은 넣지 말 것</b> — 측량면의 굴곡선은
    /// 설계가 아니라 지형이고, 수천 개가 쏟아진다.</param>
    /// <summary>선 하나(정점 목록)가 선형을 넘는 자리를 모은다 — 굴곡선·도면선 공용.
    /// <para>Civil의 선형은 AcDb 곡선이 아니라 <c>IntersectWith</c>가 없다. 대신
    /// <c>StationOffset</c>이 주는 <b>부호 있는 이격</b>을 쓴다 — 한 구간의 양 끝에서 부호가 바뀌면
    /// 그 사이에서 선형을 넘은 것이고, 이분법으로 0이 되는 자리를 좁힌다. 곡선 선형에도 그대로 통한다.</para></summary>
    private static int Crossings(CivilDb.Alignment al, IList<Point3d> vs, string why,
                                 double s0, double s1, Func<double, bool> keep,
                                 List<Mark> outp, ref int nSeg, ref int nSkip, ref int nOutside)
    {
        bool Probe(Point3d p, out double st, out double off)
        {
            st = 0; off = 0;
            try { al.StationOffset(p.X, p.Y, ref st, ref off); return true; }
            catch { return false; }
        }

        int hit = 0;
        for (int k = 1; k < vs.Count; k++)
        {
            nSeg++;
            Point3d A = vs[k - 1], B = vs[k];
            if (!Probe(A, out _, out double oA) || !Probe(B, out _, out double oB)) { nSkip++; continue; }
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

        int nSurf = 0, nBl = 0, nSeg = 0, nSkipFar = 0, nOutside = 0;
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
                                             ref nSeg, ref nSkipFar, ref nOutside);
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
                        (nSkipFar > 0 ? $" · 선형 밖 {nSkipFar}개" : ""));
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
        int nEnt = 0, nSeg = 0, nSkip = 0, nOutside = 0;
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
                Crossings(al, pts, why, s0, s1, keep, list, ref nSeg, ref nSkip, ref nOutside);
            }
        }
        catch (System.Exception ex) { log?.AppendLine($"   도면선 읽기 실패 — {ex.Message}"); }

        int dup = Dedupe(list);
        log?.AppendLine($"   도면선 [{string.Join("·", layers)}]: 객체 {nEnt}개 · 구간 {nSeg}개 → " +
                        $"교차 {list.Count + dup}개(중복 {dup}개 합침) → {list.Count}개" +
                        (nOutside > 0 ? $" · 걸러진 것 {nOutside}개" : ""));
        if (nEnt == 0) log?.AppendLine($"   ⚠레이어 [{string.Join("·", layers)}]에 선이 하나도 없다 — 부지정지를 먼저 돌려야 한다");
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

    /// <summary>측점을 'No.5+12.34' 꼴로 — 한국 종단도 관례.</summary>
    public static string Fmt(double station, double index = 20.0)
    {
        if (index <= 1e-6) return station.ToString("0.00");
        int no = (int)Math.Floor(station / index + 1e-9);
        double plus = station - no * index;
        return plus < 1e-4 ? $"No.{no}" : $"No.{no}+{plus:0.00}";
    }
}
