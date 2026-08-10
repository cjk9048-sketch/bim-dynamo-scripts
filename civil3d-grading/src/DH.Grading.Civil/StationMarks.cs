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

    /// <summary>정체인과 특수측점이 이보다 가까우면 <b>특수측점을 살리고 정체인을 지운다</b>.
    /// 0.3m 차이로 두 라벨이 겹치면 도면이 못 읽게 된다 — 그럴 바엔 'No.5' 하나가 낫다.</summary>
    public const double MergeTol = 0.5;

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

    public static List<Mark> FromProfileGradeBreaks(Transaction tr, ObjectId profileId)
    {
        var list = new List<Mark>();
        if (profileId.IsNull) return list;
        try
        {
            if (tr.GetObject(profileId, OpenMode.ForRead) is not CivilDb.Profile pr) return list;
            double s0 = pr.StartingStation, s1 = pr.EndingStation;

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
                if (d >= GradeBreakTol) cand.Add((s, d));
            }

            // ③ 붙어 있는 것끼리는 **가장 크게 꺾인 것 하나만** 남긴다
            foreach (var c in cand.OrderByDescending(x => x.D))
            {
                if (list.Any(m => Math.Abs(m.Station - c.S) < GradeBreakMinGap)) continue;
                list.Add(new Mark(c.S, "구배변화"));
            }
            list = list.OrderBy(m => m.Station).ToList();
        }
        catch { }
        return list;
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
