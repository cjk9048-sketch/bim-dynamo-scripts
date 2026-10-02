using System.Globalization;
using NetTopologySuite.Geometries;

namespace DH.Grading.Core;

/// <summary>★★★[v103.0 · JACK 0930 «절토 옹벽 뒤 성토 옹벽을 하면 절토 옹벽이 없어진다 — 남아야»] <b>옹벽 목록</b> — 도면에 저장하는 옹벽 한 칸.
/// <para>v102까지는 옹벽 결과물이 <b>한 벌</b>(가상옹벽_DH · 순수옹벽_DH · 레이어 통째로)이고 합성의 «짝»이 세션 정적 한 칸이라, 두 번째 옹벽 변환이 첫 옹벽을 지웠다.
/// 이제 옹벽마다 번호 · 이름(<see cref="WallSurfaceName"/>)을 따로 두고, 합성은 목록 전부를 합성 전 면 위에 차례로 짓는다(<see cref="WallDaylight.ComposeWalls"/>).</para>
/// <para>인코딩·디코딩은 여기(Core)에 두어 하네스가 되읽기 왕복을 잰다 — Civil은 (종류, 값)을 TypedValue로 옮기기만 한다(«검사 입력은 출하 입력»).</para></summary>
public sealed class WallRec
{
    /// <summary>번호(1부터 · 도면 단위로 늘고 다시 안 쓴다 — 계획 검토 v103 · M8).</summary>
    public int No;
    /// <summary>0 = 대기(지었는데 아직 합성 전) · 1 = 넣음(정지면_DH 합성에 들었다).</summary>
    public int State;
    public bool Up;
    /// <summary>고른 선의 단(0부터).</summary>
    public int Bench;
    /// <summary>★[v103.0 · JACK 1002 «부분 옹벽은 전체 단높이 규칙에 안 넣음»] 이 옹벽을 실제로 지은 단높이 · 소단(입력값 — 도킹창 기본값이 아니다).</summary>
    public double H, T;
    public double T0, T1;
    /// <summary>옹벽 머리 높이(폴리곤 z).</summary>
    public double HeadZ;
    /// <summary>대기 칸이 바꿀 넣음 옹벽 번호(같은 선에서 구간이 겹친 옛 옹벽 — JACK 1002 «그 옹벽을 새 값으로 바꿈») · 0이면 없음.</summary>
    public int ReplaceNo;
    /// <summary>고른 구간의 안쪽 변(자 위 점열) — 같은 선인지 가를 때 쓴다.</summary>
    public List<Point3> Seg = new();
    /// <summary>옹벽 폴리곤(첫점 반복 없음 · z = 머리) + 점마다 옹벽인가.</summary>
    public List<Point3> Poly = new();
    public List<bool> IsWall = new();
    public string WallHandle = "", PureHandle = "";
    /// <summary>옹벽 변환이 적어 둔 «합성 막음» 까닭(줄 버림 · 브레이크라인 빠짐 · 정지면 후보 여럿 …) — 비었으면 합성해도 된다.</summary>
    public string Block = "";
    /// <summary>마지막 합성 판정 글 · 시각.</summary>
    public string LastNote = "", Stamp = "";
    /// <summary>★[v103.1 · 판 2] 지을 때 잰 합성 전 면(P0) 핸들 — 지금 P0와 다르면 정지면을 다시 지은 것이라 P0 몫을 다시 짓는다. 빈 값 = 모름(판 1).</summary>
    public string P0Handle = "";
    /// <summary>★[v103.1 · 판 2 · 검토 v103 H3 «지은 값을 저장»] 이 옹벽을 지은 앞면 구배 · 앞면 최소 너비 · 원지반 재는 간격. 음수 = 모름(판 1 — 지금 값을 쓰고 로그에 적는다).</summary>
    public double SlopeW = -1, FaceRun = -1, Grid = -1;
    /// <summary>★[v103.1 · 판 2] 안쪽 변을 잰 자 — 0 단 링 · 1 계획선(자 없는 구간).</summary>
    public int RulerKind;
    /// <summary>★[v103.1 · 판 2] 보류 까닭 — 정지면을 다시 지었더니 서 있던 단이 움직였다 등. 비었으면 보류 아님. 보류 옹벽은 합성에서 빼고(지우지 않음) 겹침 판정에서 «바꿈» 대상.</summary>
    public string Hold = "";

    public string Side => Up ? "절토" : "성토";
    public static string WallSurfaceName(int no) => $"가상옹벽{no}_DH";
    public static string PureSurfaceName(int no) => $"순수옹벽{no}_DH";
}

/// <summary>목록 머리 — 구역 계획선 · 원지반 · 다음 번호.</summary>
public sealed class WallListHead
{
    public string PlanHandle = "", GroundHandle = "";
    public int NextNo = 1;
    /// <summary>★[v103.1 · 판 2 · 검토 v103.1 높음 3] 끝까지 성공한 마지막 정지면 생성이 만든 정지면_DH 핸들 — 지금 P0가 이것과 다르면 확인 안 된 면이라 더하기 · 합성이 멈춘다. 빈 값 = 모름.</summary>
    public string VerifiedP0 = "";
}

public static class WallList
{
    public const string Sign = "DH_WALLS";
    public const int Version = 2;

    /// <summary>(종류, 값) — 'S' 글 · 'I' 정수 · 'D' 실수. Civil이 DxfCode.Text · Int32 · Real로 옮긴다.</summary>
    public static List<(char Kind, object Value)> Encode(WallListHead head, IReadOnlyList<WallRec> walls)
    {
        var v = new List<(char, object)>
        {
            ('S', Sign), ('I', Version), ('S', head.PlanHandle ?? ""), ('S', head.GroundHandle ?? ""), ('I', head.NextNo), ('S', head.VerifiedP0 ?? ""), ('I', walls.Count),
        };
        void Pts(List<Point3> p) { v.Add(('I', p.Count)); foreach (var q in p) { v.Add(('D', q.X)); v.Add(('D', q.Y)); v.Add(('D', q.Z)); } }
        foreach (var w in walls)
        {
            v.Add(('I', w.No)); v.Add(('I', w.State)); v.Add(('I', w.Up ? 1 : 0)); v.Add(('I', w.Bench));
            v.Add(('D', w.H)); v.Add(('D', w.T)); v.Add(('D', w.T0)); v.Add(('D', w.T1)); v.Add(('D', w.HeadZ));
            v.Add(('I', w.ReplaceNo));
            v.Add(('S', w.WallHandle ?? "")); v.Add(('S', w.PureHandle ?? "")); v.Add(('S', w.Block ?? "")); v.Add(('S', w.LastNote ?? "")); v.Add(('S', w.Stamp ?? ""));
            Pts(w.Seg); Pts(w.Poly);
            v.Add(('I', w.IsWall.Count)); foreach (var b in w.IsWall) v.Add(('I', b ? 1 : 0));
            // 판 2
            v.Add(('S', w.P0Handle ?? "")); v.Add(('D', w.SlopeW)); v.Add(('D', w.FaceRun)); v.Add(('D', w.Grid)); v.Add(('I', w.RulerKind)); v.Add(('S', w.Hold ?? ""));
        }
        return v;
    }

    /// <summary>되읽기 — 서명 · 판 · 수가 안 맞으면 거짓(까닭). 값의 종류가 어긋나도 거짓(조용히 엉뚱한 값을 읽지 않는다).</summary>
    public static bool TryDecode(IReadOnlyList<(char Kind, object Value)> v, out WallListHead head, out List<WallRec> walls, out string why)
    {
        head = new WallListHead(); walls = new List<WallRec>(); why = "";
        int i = 0;
        try
        {
            string S() { var (k, o) = v[i++]; if (k != 'S' || o is not string s) throw new FormatException($"{i - 1}번째 값이 글이 아니다"); return s; }
            int I() { var (k, o) = v[i++]; if (k != 'I' || o is not int n) throw new FormatException($"{i - 1}번째 값이 정수가 아니다"); return n; }
            double D() { var (k, o) = v[i++]; if (k != 'D' || o is not double d) throw new FormatException($"{i - 1}번째 값이 실수가 아니다"); return d; }
            List<Point3> Pts() { int n = I(); if (n < 0 || n > 1_000_000) throw new FormatException($"점 수 {n}"); var p = new List<Point3>(n); for (int k = 0; k < n; k++) p.Add(new Point3(D(), D(), D())); return p; }
            if (S() != Sign) { why = "옹벽 목록 서명이 다르다"; return false; }
            int ver = I();
            if (ver < 1 || ver > Version) { why = $"옹벽 목록 판 {ver}(이 애드인은 {Version}) — 더 새 판이 쓴 목록"; return false; }
            head.PlanHandle = S(); head.GroundHandle = S(); head.NextNo = I();
            if (ver >= 2) head.VerifiedP0 = S();
            int nw = I();
            if (nw < 0 || nw > 10_000) { why = $"옹벽 수 {nw}"; return false; }
            for (int k = 0; k < nw; k++)
            {
                var w = new WallRec { No = I(), State = I(), Up = I() != 0, Bench = I(), H = D(), T = D(), T0 = D(), T1 = D(), HeadZ = D(), ReplaceNo = I() };
                w.WallHandle = S(); w.PureHandle = S(); w.Block = S(); w.LastNote = S(); w.Stamp = S();
                w.Seg = Pts(); w.Poly = Pts();
                int nf = I(); if (nf < 0 || nf > 1_000_000) throw new FormatException($"표 수 {nf}");
                for (int q = 0; q < nf; q++) w.IsWall.Add(I() != 0);
                if (ver >= 2) { w.P0Handle = S(); w.SlopeW = D(); w.FaceRun = D(); w.Grid = D(); w.RulerKind = I(); w.Hold = S(); }
                walls.Add(w);
            }
            if (i != v.Count) { why = $"옹벽 목록 끝에 값 {v.Count - i}개가 남았다"; return false; }
            return true;
        }
        catch (Exception ex) when (ex is ArgumentOutOfRangeException || ex is IndexOutOfRangeException)
        { why = $"옹벽 목록이 중간에 끊겼다({v.Count}개 값 중 {i}번째에서)"; walls = new List<WallRec>(); return false; }
        catch (Exception ex) when (ex is FormatException || ex is InvalidCastException)
        { why = "옹벽 목록을 못 읽었다 — " + ex.Message; walls = new List<WallRec>(); return false; }
    }

    /// <summary>★★[v103.0 · JACK 0930 인터뷰 ① · 1002 «같은 선이면 바꿈»] 새 옹벽을 목록에 넣어도 되는가.
    /// <para>①같은 방향 · 같은 단 · 안쪽 변이 <paramref name="sameLineMin"/> 넘게 겹치는 «넣음» 옹벽이 <b>하나</b>면 그 옹벽을 바꾼다(ReplaceNo).
    /// ②그 밖의 옹벽과 폴리곤이 겹치거나 <paramref name="touchTol"/> 안이면 멈춘다(규칙 ① — 떨어진 옹벽만 쌓는다 · 이어 붙이기는 다음 판).
    /// ③바꿀 후보가 둘 이상이면(새 구간이 두 옹벽에 걸친다) 멈춘다.</para></summary>
    /// <returns>(바꿀 번호 · 0이면 없음, 멈출까닭 — 비었으면 넣어도 된다, 글).</returns>
    public static (int ReplaceNo, string Stop, string Note) Conflict(IReadOnlyList<Point3> newPoly, IReadOnlyList<Point3> newSeg, bool up, int bench,
        IEnumerable<WallRec> existing, double touchTol = 0.01, double sameLineTol = 0.05, double sameLineMin = 0.5)
    {
        var ci = CultureInfo.InvariantCulture;
        if (newPoly == null || newPoly.Count < 3) return (0, "새 폴리곤이 없다", "");
        double ox = newPoly[0].X, oy = newPoly[0].Y;
        var gf = new GeometryFactory();
        Geometry? Poly(IReadOnlyList<Point3> p)
        {
            if (p == null || p.Count < 3) return null;
            var cs = p.Select(q => new Coordinate(q.X - ox, q.Y - oy)).Append(new Coordinate(p[0].X - ox, p[0].Y - oy)).ToArray();
            try { var g = gf.CreatePolygon(cs); return g.IsValid ? g : g.Buffer(0); } catch { return null; }
        }
        Geometry? Line(IReadOnlyList<Point3> p)
        {
            if (p == null || p.Count < 2) return null;
            var cs = new List<Coordinate>();
            foreach (var q in p) { var c = new Coordinate(q.X - ox, q.Y - oy); if (cs.Count == 0 || !cs[^1].Equals2D(c)) cs.Add(c); }
            return cs.Count >= 2 ? gf.CreateLineString(cs.ToArray()) : null;
        }
        var nP = Poly(newPoly);
        if (nP == null || nP.IsEmpty) return (0, "새 폴리곤을 못 만들었다", "");
        var nS = Line(newSeg);
        var same = new List<(int No, double Len)>();
        var hits = new List<string>();
        var notes = new List<string>();
        foreach (var w in existing)
        {
            if (w.State != 1) continue;
            // ★[v103.1 · 검토 v103.1 새 중간 3] 보류 옹벽(정지면을 다시 지어 자리가 움직였다)은 합성에 안 들었다 — 겹침으로 막지 않고, 같은 선이면 «바꿈» 대상으로
            if (w.Hold.Length > 0)
            {
                double hl = 0;
                if (nS != null && w.Up == up && w.Bench == bench) { var hS = Line(w.Seg); if (hS != null) { try { hl = nS.Intersection(hS.Buffer(sameLineTol)).Length; } catch { } } }
                if (hl > sameLineMin) same.Add((w.No, hl));
                else notes.Add($"보류 옹벽 {w.No}은 겹침 판정에서 뺌");
                continue;
            }
            // ① 같은 선 — 같은 방향 · 같은 단 · 안쪽 변이 겹치는 길이
            double sameLen = 0;
            if (nS != null && w.Up == up && w.Bench == bench)
            {
                var oS = Line(w.Seg);
                if (oS != null) { try { sameLen = nS.Intersection(oS.Buffer(sameLineTol)).Length; } catch { sameLen = 0; } }
            }
            if (sameLen > sameLineMin) { same.Add((w.No, sameLen)); continue; }
            // ② 다른 옹벽 — 겹침 · 맞닿음
            var oP = Poly(w.Poly);
            if (oP == null) { notes.Add($"옹벽 {w.No}의 폴리곤을 못 읽었다(판정에서 뺌)"); continue; }
            double d;
            try { d = nP.Distance(oP); } catch { d = 0; }
            if (d <= touchTol)
            {
                double ov = 0; try { ov = nP.Intersection(oP).Area; } catch { }
                hits.Add(ov > 1e-6
                    ? string.Format(ci, "옹벽 {0}({1})과 폴리곤이 {2:F1}㎡ 겹친다", w.No, w.Side, ov)
                    : string.Format(ci, "옹벽 {0}({1})과 폴리곤이 맞닿는다(거리 {2:F3}m)", w.No, w.Side, d));
            }
        }
        if (same.Count > 1)
            return (0, $"새 구간이 같은 선의 옹벽 {string.Join(" · ", same.Select(s => s.No))}에 함께 걸친다 — 한 옹벽 구간 안에서 다시 고르세요", string.Join(" · ", notes));
        if (hits.Count > 0)
            return (0, string.Join(" · ", hits) + " — 떨어진 옹벽만 쌓는다(이어 붙이기는 다음 판)", string.Join(" · ", notes));
        int rep = same.Count == 1 ? same[0].No : 0;
        if (rep > 0) notes.Insert(0, string.Format(ci, "같은 선의 옹벽 {0}과 구간이 {1:F1}m 겹친다 → 그 옹벽을 새 값으로 바꾼다", rep, same[0].Len));
        return (rep, "", string.Join(" · ", notes));
    }
}
