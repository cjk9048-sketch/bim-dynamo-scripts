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
    /// <summary>★[v103.1.14 · 판 3 · JACK 1014 ② «양쪽이 다 닿으면 셋을 하나로»] 함께 바꿀 둘째 옛 옹벽(이은 구간의 뒤 옹벽) · 0이면 없음.</summary>
    public int ReplaceNo2;
    /// <summary>★[v103.1.14 · 판 3 · JACK 1014 ② «번호 그대로»] 합성이 되면 물려받을 번호(이어 지은 옛 옹벽 번호) · 0이면 없음. 물려받으면 0으로 돌아간다.</summary>
    public int KeepNo;
    /// <summary>★★[v103.2 · 판 4 · 계획 2-2] 이 칸을 넣은 합성의 일회용 번호 — 정지면_DH 합성 표지의 넷째 값과 같으면 아래 <see cref="Cap"/>이 지금 최종면의 뚜껑이다. 빈 값 = 모름(옛 판 합성 · 아직 안 넣음).</summary>
    public string CompNonce = "";
    /// <summary>★★[v103.2 · 판 4] 합성이 남긴 뚜껑(옹벽 뒤에 되살아난 원지반 — 종단 · 횡단의 «옹벽이 든 순수면»에서 뺀다). null = 없음.</summary>
    public PureWithWalls.Cap? Cap;

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
    /// <summary>★★[v103.2 · 판 4 · 계획 2-7 · 4판 검토 중간 1] <b>굳은 옹벽</b> — «이어서»로 앞 구역 면(정지순수_DH이전)에 굳은 옹벽. 머리에 두어 목록을 읽고 다시 쓰는 모든 자리가 지킨다.
    /// «새로» · DHRESET이 지운다.</summary>
    public List<FrozenWall> Frozen = new();
}

/// <summary>★★[v103.2 · 판 4] 굳은 옹벽 한 칸 — 종단 측점 가리기 · 걷기에 쓴다(번호 · 구역 계획선 · 폴리곤 · 방향 · 앞면 구배 · 앞면 최소 너비 · 단높이 · 순수면에 반영됐나).</summary>
public sealed class FrozenWall
{
    public int No;
    public string PlanHandle = "";
    public bool Up;
    public double H, SlopeW = -1, FaceRun = -1;
    /// <summary>순수면(정지순수_DH이전)에 반영됐나 — «반영 없이 이어서»로 굳은 것은 거짓(가리지도 걷지도 않고 ⚠).</summary>
    public bool Reflected;
    public List<Point3> Poly = new();
    /// <summary>★★[v103.3 · 판 5 · 계획 v103.3 2-2] 옹벽 터파기가 앞면 선을 다시 짓는 재료 — 점마다 옹벽인가 · 소단 · 머리 높이.
    /// 빈 <see cref="IsWall"/> = 판 4 굳은 칸(옛 기록 — 옹벽선을 몰라 터파기에서 빠지고 ⚠).</summary>
    public List<bool> IsWall = new();
    public double T, HeadZ;
    public bool HasLine => IsWall.Count == Poly.Count && Poly.Count >= 3;
}

public static class WallList
{
    public const string Sign = "DH_WALLS";
    public const int Version = 5;

    /// <summary>(종류, 값) — 'S' 글 · 'I' 정수 · 'D' 실수. Civil이 DxfCode.Text · Int32 · Real로 옮긴다.
    /// <para>★[v103.1.14] 판 3 칸(ReplaceNo2 · KeepNo)은 <b>이어 짓기가 걸려 있을 때만</b> 쓴다 — 둘 다 0인 목록은 판 2 그대로(바이트까지 같음 · 옛 판 애드인도 읽는다).</para>
    /// <para>★[v103.2] 판 4 칸(CompNonce · Cap · 굳은 옹벽)은 <b>v103.2 합성이 한 번이라도 넣은 칸이 있거나 굳은 칸이 있을 때만</b> 쓴다(4판 검토 낮음 2 — Cap 유무가 아니라 CompNonce로 가른다).</para></summary>
    public static List<(char Kind, object Value)> Encode(WallListHead head, IReadOnlyList<WallRec> walls)
    {
        // ★[v103.3] 판 5 = 옹벽선을 가진 굳은 칸이 있을 때만(없으면 판 4 바이트 그대로 — v103.2 애드인도 읽는다)
        int ver = head.Frozen.Any(f => f.IsWall.Count > 0) ? 5
                : walls.Any(w => !string.IsNullOrEmpty(w.CompNonce)) || head.Frozen.Count > 0 ? 4
                : walls.Any(w => w.ReplaceNo2 != 0 || w.KeepNo != 0) ? 3 : 2;
        var v = new List<(char, object)>
        {
            ('S', Sign), ('I', ver), ('S', head.PlanHandle ?? ""), ('S', head.GroundHandle ?? ""), ('I', head.NextNo), ('S', head.VerifiedP0 ?? ""), ('I', walls.Count),
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
            if (ver >= 3) { v.Add(('I', w.ReplaceNo2)); v.Add(('I', w.KeepNo)); }
            if (ver >= 4)
            {
                v.Add(('S', w.CompNonce ?? ""));
                if (w.Cap == null) v.Add(('I', -1));
                else
                {
                    v.Add(('I', w.Cap.Count)); v.Add(('D', w.Cap.Area)); v.Add(('S', w.Cap.Hash.ToString(CultureInfo.InvariantCulture)));
                    v.Add(('I', w.Cap.Edges.Count));
                    foreach (var (x1, y1, x2, y2) in w.Cap.Edges) { v.Add(('D', x1)); v.Add(('D', y1)); v.Add(('D', x2)); v.Add(('D', y2)); }
                }
            }
        }
        if (ver >= 4)
        {
            v.Add(('I', head.Frozen.Count));
            foreach (var f in head.Frozen)
            {
                v.Add(('I', f.No)); v.Add(('S', f.PlanHandle ?? "")); v.Add(('I', f.Up ? 1 : 0)); v.Add(('D', f.H)); v.Add(('D', f.SlopeW)); v.Add(('D', f.FaceRun)); v.Add(('I', f.Reflected ? 1 : 0));
                Pts(f.Poly);
                if (ver >= 5)
                {
                    v.Add(('I', f.IsWall.Count)); foreach (var b in f.IsWall) v.Add(('I', b ? 1 : 0));
                    v.Add(('D', f.T)); v.Add(('D', f.HeadZ));
                }
            }
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
                if (ver >= 3) { w.ReplaceNo2 = I(); w.KeepNo = I(); }
                if (ver >= 4)
                {
                    w.CompNonce = S();
                    int cn = I();
                    if (cn >= 0)
                    {
                        var cap = new PureWithWalls.Cap { Count = cn, Area = D() };
                        if (!long.TryParse(S(), NumberStyles.Integer, CultureInfo.InvariantCulture, out cap.Hash)) throw new FormatException("뚜껑 해시");
                        int ne = I(); if (ne < 0 || ne > 10_000_000) throw new FormatException($"뚜껑 변 수 {ne}");
                        for (int q = 0; q < ne; q++) cap.Edges.Add((D(), D(), D(), D()));
                        w.Cap = cap;
                    }
                }
                walls.Add(w);
            }
            if (ver >= 4)
            {
                int nf2 = I(); if (nf2 < 0 || nf2 > 10_000) throw new FormatException($"굳은 옹벽 수 {nf2}");
                for (int q = 0; q < nf2; q++)
                {
                    var f = new FrozenWall { No = I(), PlanHandle = S(), Up = I() != 0, H = D(), SlopeW = D(), FaceRun = D(), Reflected = I() != 0, Poly = Pts() };
                    if (ver >= 5)
                    {
                        int nw2 = I(); if (nw2 < 0 || nw2 > 1_000_000) throw new FormatException($"굳은 옹벽 표 수 {nw2}");
                        for (int b = 0; b < nw2; b++) f.IsWall.Add(I() != 0);
                        f.T = D(); f.HeadZ = D();
                    }
                    head.Frozen.Add(f);
                }
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
        var r = Conflict(newPoly, newSeg, up, bench, existing, null, touchTol, sameLineTol, sameLineMin);
        return (r.ReplaceNo, r.Stop, r.Note);
    }

    /// <summary>★★★[v103.1.14 · JACK 1014 ② «맞닿은 옹벽은 단높이·소단이 같을 때만 늘려 한 옹벽으로 · 양쪽이 다 닿으면 셋을 하나로»] <see cref="Conflict(IReadOnlyList{Point3}, IReadOnlyList{Point3}, bool, int, IEnumerable{WallRec}, double, double, double)"/>에
    /// <b>이어 지을 옹벽</b>(<paramref name="extend"/> — 이은 구간의 앞 · 뒤 차례 · 판정은 <see cref="WallPlace.ExtendPlan"/>)을 더한다.
    /// <para>이어 지을 옹벽은 새 폴리곤(이은 구간 전체)의 같은 선 겹침으로 <b>모두</b> 잡혀야 하고, 같은 선에 잡힌 옹벽이 그 밖에 더 있으면 멈춘다. 그 밖의 옹벽과의 겹침 · 맞닿음은 종전처럼 멈춘다.
    /// <paramref name="extend"/>가 null이면 종전과 같다.</para></summary>
    /// <returns>(바꿀 번호 — 이어 짓기면 앞 옹벽, 둘째로 바꿀 번호 — 셋을 하나로일 때 뒤 옹벽, 멈출까닭, 글).</returns>
    public static (int ReplaceNo, int ReplaceNo2, string Stop, string Note) Conflict(IReadOnlyList<Point3> newPoly, IReadOnlyList<Point3> newSeg, bool up, int bench,
        IEnumerable<WallRec> existing, IReadOnlyList<int>? extend, double touchTol = 0.01, double sameLineTol = 0.05, double sameLineMin = 0.5)
    {
        var ext = extend != null && extend.Count > 0 ? extend : null;
        var ci = CultureInfo.InvariantCulture;
        if (newPoly == null || newPoly.Count < 3) return (0, 0, "새 폴리곤이 없다", "");
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
        if (nP == null || nP.IsEmpty) return (0, 0, "새 폴리곤을 못 만들었다", "");
        var nS = Line(newSeg);
        var same = new List<(int No, double Len)>();
        var hits = new List<string>();
        var notes = new List<string>();
        var ex = existing as IReadOnlyCollection<WallRec> ?? existing.ToList();
        // ★[v103.1.12 · 계획 3판 중간 4 · 4판 중간 1] 미룬 옹벽이 바꿀 옛 옹벽(합성이 되면 지운다)은 판정에서 뺀다 — 같은 선을 다시 고르면 그 미룬 옹벽이 «바꿈» 대상이다
        //   (미룬 옹벽이 «합성 실패»로 보류여도 — 새 옹벽이 그 미룬 옹벽을 바꾸고, 합성은 바꿈을 이어 따라가 옛 옹벽까지 뺀다: ReplacedBy)
        var goingAway = GoingAway(ex);
        foreach (var w in ex)
        {
            if (w.State != 1) continue;
            // ★[v103.1.12 · 코드 검토 낮음 7] 바뀔 옛 옹벽은 <b>같은 선의 바꿈 대상에서만</b> 뺀다 — 같은 선이 아닌 새 옹벽과의 겹침은 그대로 본다
            //   (바꾸려던 미룬 옹벽이 보류로 남으면 옛 옹벽은 합성에 그대로 있다 — 그 자리와 겹치는 새 옹벽은 막아야 한다)
            if (goingAway.Contains(w.No))
            {
                double gl = 0;
                if (nS != null && w.Up == up && w.Bench == bench) { var gS = Line(w.Seg); if (gS != null) { try { gl = nS.Intersection(gS.Buffer(sameLineTol)).Length; } catch { } } }
                if (gl > sameLineMin) { notes.Add($"옹벽 {w.No}은 미룬 옹벽이 바꿀 옹벽이라 같은 선 바꿈 대상에서 뺌"); continue; }
            }
            // ★[v103.1 · 검토 v103.1 새 중간 3] 보류 옹벽(정지면을 다시 지어 자리가 움직였다)은 합성에 안 들었다 — 겹침으로 막지 않고, 같은 선이면 «바꿈» 대상으로
            // ★[v103.1.12 · 계획 3판 중간 7] 움직인 보류만 — «다시 짓기 실패» · «합성 실패» 보류는 자리가 그대로라 보통 옹벽처럼 겹침을 본다
            if (IsMovedHold(w.Hold))
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
        string nt = string.Join(" · ", notes);
        // ★[v103.1.14] 이어 짓기 — 같은 선에 잡힌 옹벽 집합이 이어 지을 옹벽 집합과 같아야 한다(빠지면 이은 폴리곤이 그 옹벽을 못 덮었다 · 더 있으면 다른 옹벽에 걸쳤다)
        if (ext != null)
        {
            var got = new HashSet<int>(same.Select(s => s.No));
            var miss = ext.Where(n => !got.Contains(n)).ToList();
            var extra = same.Where(s => !ext.Contains(s.No)).Select(s => s.No).ToList();
            if (miss.Count > 0)
                return (0, 0, $"이어 지을 옹벽 {string.Join(" · ", miss)}이 새 폴리곤과 같은 선에서 안 잡혔다(안쪽 변이 {sameLineMin:0.#}m 넘게 겹쳐야 한다) — 이어 짓지 않고 멈춘다", nt);
            if (extra.Count > 0)
                return (0, 0, $"이은 구간이 이어 지을 옹벽 밖의 같은 선 옹벽 {string.Join(" · ", extra)}에도 걸친다 — 이어 짓지 않고 멈춘다", nt);
            if (hits.Count > 0)
                return (0, 0, string.Join(" · ", hits) + " — 떨어진 옹벽만 쌓는다(이어 짓는 것은 같은 선 끝에 맞닿은 옹벽뿐)", nt);
            notes.Insert(0, string.Join(" · ", ext.Select(n => string.Format(ci, "같은 선의 옹벽 {0}에 이어 짓는다(겹친 안쪽 변 {1:F1}m)", n, same.First(s => s.No == n).Len))));
            return (ext[0], ext.Count > 1 ? ext[1] : 0, "", string.Join(" · ", notes));
        }
        if (same.Count > 1)
            return (0, 0, $"새 구간이 같은 선의 옹벽 {string.Join(" · ", same.Select(s => s.No))}에 함께 걸친다 — 한 옹벽 구간 안에서 다시 고르세요", nt);
        if (hits.Count > 0)
            return (0, 0, string.Join(" · ", hits) + " — 떨어진 옹벽만 쌓는다(같은 선 끝에 맞닿은 옹벽은 단높이 · 소단이 같을 때 옹벽 변환이 이어 짓는다)", nt);
        int rep = same.Count == 1 ? same[0].No : 0;
        if (rep > 0) notes.Insert(0, string.Format(ci, "같은 선의 옹벽 {0}과 구간이 {1:F1}m 겹친다 → 그 옹벽을 새 값으로 바꾼다", rep, same[0].Len));
        return (rep, 0, "", string.Join(" · ", notes));
    }

    // ══ ★★★[v103.1.12 · JACK 1014 «합성 미루기» · 1015 «그 옹벽만 보류 · 최종면이 필요한 일 직전엔 묻기»] 합성 셈 ══
    //   부르는 곳(정지 창 «대기 N» · [옹벽 합성] · 내보내기 · 터파기 · «이어서» 관문 · 자가점검)이 <b>같은 함수</b>로 센다 — 하네스 S158

    /// <summary>보류 까닭의 머리 — 이 둘은 «자리는 그대로 · 다음 합성이 다시 해 보는 보류». 그 밖의 보류는 서 있던 단이 움직인 것(사면 변환으로 되돌리거나 다시 지정).</summary>
    public const string RebuildFailTag = "다시 짓기 실패";
    public const string CompFailTag = "합성 실패";
    public static bool IsRetryHold(string? hold) => hold != null && (hold.StartsWith(RebuildFailTag, StringComparison.Ordinal) || hold.StartsWith(CompFailTag, StringComparison.Ordinal));
    public static bool IsMovedHold(string? hold) => !string.IsNullOrEmpty(hold) && !IsRetryHold(hold);
    /// <summary>미룬 옹벽 — «넣음»인데 합성에 든 적이 없다. <see cref="WallRec.LastNote"/>는 합성이 성공할 때만 채운다(DHWALLCOMP ⑨ — 처음 짓는 옹벽 변환은 빈 값).</summary>
    public static bool IsDeferred(WallRec w) => w.State == 1 && string.IsNullOrEmpty(w.LastNote);

    /// <summary>★[v103.1.12 · 계획 4판 중간 1] 미룬 옹벽(움직인 보류 아님 — «합성 실패» 보류 포함)이 바꿀 옛 옹벽 번호. 겹침 판정 · 조각 끊기가 뺀다(같은 선을 다시 고르면 그 미룬 옹벽이 바꿈 대상).</summary>
    public static HashSet<int> GoingAway(IEnumerable<WallRec> walls)
        => new(walls.Where(x => IsDeferred(x) && !IsMovedHold(x.Hold)).SelectMany(x => Replaces(x)));

    /// <summary>★[v103.1.14] 이 칸이 바꿀 옛 옹벽 번호(0 뺌) — ReplaceNo · ReplaceNo2(셋을 하나로의 뒤 옹벽).</summary>
    public static IEnumerable<int> Replaces(WallRec w)
    {
        if (w.ReplaceNo > 0) yield return w.ReplaceNo;
        if (w.ReplaceNo2 > 0) yield return w.ReplaceNo2;
    }

    /// <summary>★★[v103.1.14 · JACK 1014 ② «번호 그대로»] 합성 차례 열쇠 — 물려받을 번호가 있으면 그 번호(이어 지은 옹벽이 옛 옹벽 자리 차례에 든다 · 번호를 물려받은 뒤 다음 합성과 <b>같은 차례</b>) · 없으면 제 번호.
    /// 합성은 차례대로 쌓아 차례가 바뀌면 결과가 조금 달라질 수 있다(같으면 제 번호 차례).</summary>
    public static int ComposeKey(WallRec w) => w.KeepNo > 0 ? w.KeepNo : w.No;

    /// <summary>★★[v103.1.14] 번호 물려받기(목록) — <paramref name="from"/> 칸이 <paramref name="to"/> 번호가 된다(물려받을 번호 · 바꿈 칸은 비운다 — 이미 지운 옛 옹벽을 가리킨다) ·
    /// 다른 칸이 <paramref name="from"/>을 바꾸려 했으면 <paramref name="to"/>를 가리키게. <paramref name="to"/> 칸이 아직 목록에 있으면(옛 옹벽을 안 지웠다) 거짓 — 아무것도 안 바꾼다.</summary>
    public static bool Renumber(List<WallRec> walls, int from, int to)
    {
        if (from == to || walls.Any(w => w.No == to)) return false;
        var me = walls.FirstOrDefault(w => w.No == from);
        if (me == null) return false;
        me.No = to; me.KeepNo = 0; me.ReplaceNo = 0; me.ReplaceNo2 = 0;
        foreach (var w in walls)
        {
            if (ReferenceEquals(w, me)) continue;
            if (w.ReplaceNo == from) w.ReplaceNo = to;
            if (w.ReplaceNo2 == from) w.ReplaceNo2 = to;
            if (w.KeepNo == from) w.KeepNo = to;
        }
        return true;
    }

    // ── ★[v103.1.12 · 계획 4판 중간 6] «합성 실패» 보류의 열쇠 — 같은 정지면 · 같은 이웃이면 다시 해 봐도 같다(합성은 같은 입력에 같은 답) → 다시 안 해 본다 ──
    static readonly System.Text.RegularExpressions.Regex CompFailHead = new(@"^합성 실패\(P0 ([^ )]*) · 열쇠 ([0-9a-f]{16})\)");

    /// <summary>열쇠 — 지금 P0 + 이 옹벽의 면 · 지은 P0 + 다른 옹벽마다 번호 · 상태 · 보류 종류 · 지은 P0 · 면 핸들 · 합성 글 · 바꿈(FNV-1a 64비트 · 16자리).</summary>
    public static string CompFailKey(IEnumerable<WallRec> walls, int selfNo, string p0)
    {
        ulong h = 1469598103934665603UL;
        void Add(string s) { foreach (char c in s) { h ^= c; h *= 1099511628211UL; } h ^= 0x1F; h *= 1099511628211UL; }
        Add(p0 ?? "");
        foreach (var w in walls.OrderBy(w => w.No))
        {
            // ★[v103.1.14] 판 3 칸은 0이 아닐 때만 붙인다 — 이어 짓기와 상관없는 옹벽의 열쇠는 종전과 같다(지난 «합성 실패» 보류가 판을 올렸다고 다시 해 보지 않게)
            string x3 = w.ReplaceNo2 != 0 || w.KeepNo != 0 ? $":{w.ReplaceNo2}:{w.KeepNo}" : "";
            if (w.No == selfNo) { Add($"self:{w.No}:{w.WallHandle}:{w.P0Handle}:{w.ReplaceNo}{x3}"); continue; }
            char hk = w.Hold.Length == 0 ? 'n' : IsMovedHold(w.Hold) ? 'm' : w.Hold.StartsWith(RebuildFailTag, StringComparison.Ordinal) ? 'r' : 'c';
            Add($"{w.No}:{w.State}:{hk}:{w.P0Handle}:{w.WallHandle}:{w.LastNote}:{w.ReplaceNo}{x3}");
        }
        return h.ToString("x16");
    }

    /// <summary>«합성 실패» 보류 글 — «합성 실패(P0 h · 열쇠 k) — 까닭».</summary>
    public static string CompFailHold(string p0, string key, string why) => $"{CompFailTag}(P0 {p0} · 열쇠 {key}) — {why}";

    /// <summary>보류 글의 까닭(« — » 뒤) — 열쇠를 다시 적을 때 쓴다.</summary>
    public static string HoldWhy(string hold)
    {
        int i = hold.IndexOf(" — ", StringComparison.Ordinal);
        return i >= 0 ? hold.Substring(i + 3) : hold;
    }

    /// <summary>«합성 실패» 보류를 지금 다시 해 볼 만한가 — 정지면(P0)이나 이웃이 바뀌었거나 열쇠를 못 읽으면 참. «합성 실패» 보류가 아니면 거짓.</summary>
    public static bool CompFailFresh(WallRec w, IEnumerable<WallRec> walls, string p0)
    {
        if (!w.Hold.StartsWith(CompFailTag, StringComparison.Ordinal)) return false;
        var m = CompFailHead.Match(w.Hold);
        if (!m.Success) return true;
        if (m.Groups[1].Value != (p0 ?? "")) return true;
        return m.Groups[2].Value != CompFailKey(walls, w.No, p0 ?? "");
    }

    /// <summary>지금 정지면_DH와 옹벽 목록을 견준 셈(번호는 작은 차례).</summary>
    public sealed class CompState
    {
        /// <summary>합성을 해 볼 옹벽 — «넣음» · 움직인 보류 아님(보류 없음 · 다시 짓기 실패 · 합성 실패) + 합성이 안 막힌 «대기».</summary>
        public List<int> Target = new();
        /// <summary>대상인데 지금 정지면_DH의 합성 표지에 없다(표지가 없거나 v102 표지면 대상 전부).</summary>
        public List<int> Pending = new();
        /// <summary>표지에는 있는데 «넣음»이 아니거나 목록에 없다(지워짐 · 바뀜).</summary>
        public List<int> Stale = new();
        /// <summary>미룬 옹벽(합성에 든 적 없음).</summary>
        public List<int> Deferred = new();
        /// <summary>정지면_DH에서 빠진 옹벽 — 표지에 없는 «넣음» 전부(보류 포함) · 합성이 안 막힌 «대기» · 낡은 표지 번호.</summary>
        public List<int> Missing = new();
        /// <summary>움직인 보류 · 다시 해 볼 보류(다시 짓기 실패 · 합성 실패).</summary>
        public List<int> HeldMoved = new(), HeldRetry = new();
        /// <summary>«합성 실패» 보류인데 정지면 · 이웃이 그대로라 다시 해 봐도 같은 것(대상이 아니다 — 빠짐에는 든다).</summary>
        public List<int> Stuck = new();
        /// <summary>합성을 다시 해야 하는가 — 대상이 표지에 없거나 표지가 낡았다.</summary>
        public bool Need => Pending.Count + Stale.Count > 0;
        public string Text => $"대상 {L(Target)} · 표지에 없음 {L(Pending)} · 낡은 표지 {L(Stale)} · 미룸 {L(Deferred)} · 빠짐 {L(Missing)} · 보류(움직임 {L(HeldMoved)} · 다시 해 볼 {L(HeldRetry)}{(Stuck.Count > 0 ? $" · 그중 그대로라 안 해 봄 {L(Stuck)}" : "")}) → {(Need ? "합성 필요" : "합성 할 일 없음")}";
        static string L(List<int> l) => l.Count == 0 ? "0" : $"{l.Count}({string.Join(",", l)})";
    }

    /// <param name="tagNos">정지면_DH 합성 표지의 든 번호 — null = 정지면_DH가 합성이 아니다(또는 못 찾음) · 빈 배열 = v102 표지(번호 없음).</param>
    /// <param name="p0">지금 합성 전 면(P0) 핸들 — 주면 «합성 실패» 보류 가운데 정지면 · 이웃이 그대로인 것을 대상에서 뺀다(<see cref="CompFailFresh"/>). null이면 늘 대상.</param>
    public static CompState CompositeState(IEnumerable<WallRec> walls, int[]? tagNos, string? p0 = null)
    {
        var s = new CompState();
        var ws = walls.OrderBy(w => w.No).ToList();
        var tag = tagNos == null || tagNos.Length == 0 ? null : new HashSet<int>(tagNos);
        var ins = new HashSet<int>();
        foreach (var w in ws)
        {
            if (w.State == 1)
            {
                ins.Add(w.No);
                bool stuck = p0 != null && w.Hold.StartsWith(CompFailTag, StringComparison.Ordinal) && !CompFailFresh(w, ws, p0);
                if (stuck) s.Stuck.Add(w.No);
                bool moved = IsMovedHold(w.Hold) || stuck;
                bool inTag = tag != null && tag.Contains(w.No);
                if (!moved) s.Target.Add(w.No);
                if (IsDeferred(w)) s.Deferred.Add(w.No);
                if (IsMovedHold(w.Hold)) s.HeldMoved.Add(w.No); else if (w.Hold.Length > 0) s.HeldRetry.Add(w.No);
                if (!inTag) { s.Missing.Add(w.No); if (!moved) s.Pending.Add(w.No); }
            }
            else if (string.IsNullOrEmpty(w.Block))
            {
                // 합성이 안 막힌 «대기» — 합성을 이어 태우지 못했다(합성이 늘 받아 «넣음»으로 바꾸거나 버린다)
                s.Target.Add(w.No); s.Pending.Add(w.No); s.Missing.Add(w.No);
            }
        }
        if (tag != null)
            foreach (int n in tag.OrderBy(x => x))
                if (!ins.Contains(n)) { s.Stale.Add(n); s.Missing.Add(n); }
        return s;
    }

    /// <summary>합성에 넣을 새 옹벽들(«대기» · 미룬 옹벽)이 바꾸는 옛 옹벽 번호 — 바꿈을 이어 따라간다(새 옹벽 → 바꾸려던 미룬 옹벽 → 그 미룬 옹벽이 바꾸려던 옛 옹벽).
    /// 미룬 옹벽이 없으면 종전(«대기»의 ReplaceNo)과 같다.</summary>
    public static HashSet<int> ReplacedBy(IEnumerable<WallRec> walls, IEnumerable<int> newNos)
    {
        var byNo = new Dictionary<int, WallRec>();
        foreach (var w in walls) if (!byNo.ContainsKey(w.No)) byNo[w.No] = w;
        var res = new HashSet<int>();
        var todo = new Stack<int>();
        foreach (int n in newNos) if (byNo.TryGetValue(n, out var w)) foreach (int r0 in Replaces(w)) todo.Push(r0);
        while (todo.Count > 0)
        {
            int r = todo.Pop();
            if (!res.Add(r)) continue;
            if (byNo.TryGetValue(r, out var rw) && IsDeferred(rw)) foreach (int r1 in Replaces(rw)) todo.Push(r1);
        }
        return res;
    }

    /// <summary>합성 관문이 «같은 것을 이미 해 봤다»를 가르는 지문 — 옹벽마다 번호 · 상태 · 보류 · 지은 P0 · 면 핸들 · 합성 글 + 지금 P0 · 표지 번호.</summary>
    public static string StateSig(IEnumerable<WallRec> walls, string p0, int[]? tagNos)
    {
        var sb = new System.Text.StringBuilder(p0 ?? "").Append('|').Append(tagNos == null ? "-" : string.Join(",", tagNos));
        foreach (var w in walls.OrderBy(w => w.No))
            sb.Append('|').Append(w.No).Append(':').Append(w.State).Append(':').Append(w.Hold).Append(':').Append(w.P0Handle).Append(':').Append(w.WallHandle).Append(':').Append(w.LastNote).Append(':').Append(w.ReplaceNo)
              .Append(':').Append(w.ReplaceNo2).Append(':').Append(w.KeepNo);
        return sb.ToString();
    }
}
