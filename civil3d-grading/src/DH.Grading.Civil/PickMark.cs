using System.Collections.Generic;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.Geometry;
using Autodesk.AutoCAD.GraphicsInterface;
using AcadApp = Autodesk.AutoCAD.ApplicationServices.Application;

namespace DH.Grading.Civil;

/// <summary>★★★[JACK 0908] <b>고른 것을 빨갛게 — 도면은 건드리지 않는다.</b>
///
/// <para>JACK: <i>"계획폴리곤 선택 시 선택한 걸 알 수 있도록 빨간색으로 색 바뀌고,
/// 정지가 끝나면 다시 원래색으로 복귀. (구조물 터파기도 마찬가지)"</i></para>
///
/// <para><b>첫 판은 객체의 색을 진짜로 바꿨다가 갈아엎었다.</b> 검토가 셋을 짚었다:</para>
/// <list type="number">
/// <item><b>피처라인은 색이 안 바뀐다.</b> Civil 객체는 제 색을 <b>스타일</b>이 정한다 —
///   <c>ColorIndex</c>를 1로 놓아도 화면은 그대로인데 <b>성공했다고 말한다</b>.
///   두 명령 다 피처라인을 고를 수 있게 열어 두었으니 실제로 겪는 길이다.</item>
/// <item><b>원래 색을 못 적었는데도 칠했다.</b> 기록이 실패하면 되돌릴 방법이 사라져
///   <b>폴리곤이 영구히 빨갛게</b> 남는다 — 세 겹 방어가 통째로 무력해진다.</item>
/// <item><b>되돌리기 전에 기록을 지웠다.</b> 레이어가 잠겨 있으면 색은 못 바꾸고
///   기록만 사라져 <b>다시는 못 고친다</b>.</item>
/// </list>
///
/// <para>→ <b>도면을 아예 안 건드린다.</b> 고른 것의 <b>모양만 베껴</b> 빨간 임시 그래픽으로 덧그린다.
/// <b>원래 색이라는 것이 없으므로 되돌릴 것도 없다</b> — 위 셋이 한꺼번에 사라진다.
/// 객체 종류도 안 가리고(<see cref="BoundaryReader"/>가 셋 다 읽는다),
/// 프로세스가 죽어도 임시 그래픽은 <b>저장되지 않으므로</b> 자국이 안 남는다.</para>
///
/// <para><b>★화면에서 한 번은 확인해야 한다.</b> 이 저장소에 임시 그래픽 선례가 없다 —
/// 모드가 화면에 보이느냐는 <b>읽어서 못 정한다</b>. 정지를 띄워 빨간 선이 나온 뒤
/// <b>휠 확대·화면이동·REGEN</b> 셋을 하고도 선이 남아 있어야 한다.</para></summary>
internal sealed class PickMark : System.IDisposable
{
    /// <summary>고른 것을 칠할 색 — 빨강.</summary>
    private const short PickAci = 1;

    /// <summary>★★[JACK 0909 <i>"줄이는 것보다 두껍게 하는 건 어때?"</i>] <b>띠의 굵기 — 도형 대각선의 몇 %인가.</b>
    ///
    /// <para><b>왜 굵기인가.</b> 빨간 표시는 고른 폴리선과 <b>똑같은 자리</b>에 있어
    /// 어느 쪽이 나중에 그려지느냐로 보이고 안 보이고가 갈린다. 그 순서는 우리가 못 정한다.
    /// 그런데 <b>굵게 그리면 순서를 이길 필요가 없다</b> — 뒤에 있어도 얇은 흰 선 <b>양옆으로</b>
    /// 빨간 띠가 삐져나오기 때문이다. JACK의 생각이 맞았고 동그라미보다 이쪽이 낫다.</para>
    ///
    /// <para><b>왜 비율인가.</b> 화면 배율을 알 수 없다. 고정 굵기로 두면 작은 밸브실에서는
    /// 도형을 덮어 버리고 큰 부지에서는 실오라기가 된다. 사용자는 어차피 그 도형이 보이게
    /// 확대해 놓고 보므로 <b>도형 크기에 맞추는 것</b>이 언제나 비슷하게 보인다.</para>
    ///
    /// <para>★<b>이 값은 그림에만 쓴다</b> — 정지·터파기 계산은 이것을 <b>쳐다보지도 않는다</b>
    /// (JACK 0909 확인). 0으로 두어 띠를 없애도 결과는 소수점 한 자리도 안 바뀐다.</para></summary>
    private const double BandFrac = 0.005;

    /// <summary>띠를 만들 수 없을 만큼 작은 도형일 때의 굵기(m).</summary>
    private const double BandMin = 0.05;

    /// <summary>★[검토 0909 · 높음] 띠 조각의 <b>상한</b> — 이 위로는 솎아 그린다.</summary>
    private const int MaxBands = 300;

    /// <summary>모서리 메움 동그라미의 <b>상한</b>.</summary>
    private const int MaxDots = 200;

    private readonly List<Drawable> _marks = new();

    /// <summary>★★★[계획 3단계] <b>이 표시가 사는 도면.</b>
    ///
    /// <para><b>왜 필요한가.</b> <c>TransientManager.CurrentTransientManager</c>는
    /// <b>지금 보고 있는 도면</b>의 것이다. 종전엔 명령이 끝날 때 바로 걷었으므로
    /// 도면이 바뀔 틈이 없었다 — 그런데 <b>도킹창이 표시를 들고 있게</b> 되면
    /// 사용자가 언제든 다른 도면으로 넘어간다.</para>
    ///
    /// <para>그때 걷으라고 시키면 <b>새 도면의 관리자에게 옛 도면 객체를 걷으라</b>고 하는 꼴이다 —
    /// <c>false</c>가 돌아오고, 그것을 못 보면 해제까지 해서 <b>옛 도면 관리자가 죽은 포인터를 쥔다</b>.
    /// 그 도면으로 돌아가 화면이 갱신되는 순간 AutoCAD가 죽는다.</para>
    ///
    /// <para>→ <b>내 도면이 아니면 아무것도 안 한다</b>(걷지도, 해제하지도 않는다).
    /// 관리 객체가 새는 것이 AutoCAD가 죽는 것보다 낫다 — 이 파일의 기존 규칙 그대로다.</para></summary>
    private Autodesk.AutoCAD.ApplicationServices.Document _doc;

    /// <summary>도면이 달라 <b>손대지 않고 버린</b> 횟수 — 일어났는지조차 모르면 안 된다.</summary>
    internal static int StrandedCount => _stranded;
    private static int _stranded;

    /// <summary>★★★[검토 0909 · 치명] <b>못 걷은 임시선을 붙잡아 두는 자리.</b>
    ///
    /// <para><b>내가 정확히 반대로 했다.</b> 아래 <see cref="Clear"/>에 <i>"놓아 주지 않는다 —
    /// 관리자가 아직 쥐고 있다"</i>고 써 놓고 <c>_marks.Clear()</c>를 불렀는데,
    /// 그것이 바로 <b>마지막 관리 참조를 버리는 것</b>이다.</para>
    ///
    /// <para><c>Line</c>은 <c>DisposableWrapper</c>를 물려받고, 도면에 넣지 않은 것은
    /// <c>AutoDelete</c>가 참이라 <b>쓰레기 수집기가 소멸자에서 네이티브 객체를 지운다</b>.
    /// 그런데 임시 그래픽 관리자는 <b>그 포인터를 아직 쥐고 있다</b> —
    /// 몇 초~몇 분 뒤 그 도면을 다시 그리는 순간 AutoCAD가 <b>예외도 로그도 없이</b> 사라진다.
    /// <b>막겠다던 바로 그 죽은 포인터를 내가 만든 것이다.</b></para>
    ///
    /// <para>→ <b>정적 뿌리에 붙잡아 둔다.</b> 관리 객체 몇 개가 새는 것이
    /// AutoCAD가 죽는 것보다 낫다 — 이 파일이 처음부터 세운 규칙 그대로,
    /// 이제 <b>말만이 아니라 실제로</b> 그렇게 한다.</para>
    ///
    /// <para>도면이 <b>완전히 닫힐 때</b>는 관리자도 같이 사라지므로 그때 놓아 준다
    /// (<see cref="ReleaseFor"/>).</para></summary>
    private static readonly List<(object Doc, Drawable D)> _held = new();

    /// <summary>지금 붙잡고 있는 개수 — 자가검증에 찍는다.</summary>
    internal static int HeldCount => _held.Count;

    /// <summary>★★★[검토 0909 · 치명] <b>붙잡는 유일한 문 — 소멸자를 여기서 끈다.</b>
    ///
    /// <para><b>정적 뿌리에 담는 것만으로는 모자랐다.</b> 그것은 GC가 <b>언제</b> 거두느냐를
    /// 미룰 뿐이고, 도면이 닫혀 목록을 비우는 순간 소멸자가 되살아난다.
    /// <c>Line</c>·<c>Solid</c>·<c>Circle</c>은 도면에 안 넣었으므로 <c>AutoDelete</c>가 참이라
    /// <b>소멸자가 네이티브를 진짜로 지운다</b> — 관리자가 그 포인터를 쥔 채로.</para>
    ///
    /// <para>→ <c>SuppressFinalize</c>로 <b>소멸자 자체를 끈다.</b> 그러면 목록을 비우든,
    /// AutoCAD가 먼저 네이티브를 지우든, <b>두 번 지우는 일이 없다</b>.
    /// 관리 객체 몇 개가 새는 것이 AutoCAD가 죽는 것보다 낫다 — 이 파일의 규칙 그대로.</para></summary>
    private static void Hold(object doc, Drawable d)
    {
        try { System.GC.SuppressFinalize(d); } catch { }
        _held.Add((doc, d));
    }

    /// <summary>★★★[검토 0909 · 치명] 그 도면이 닫혔다 — <b>목록에서만 뺀다.</b>
    ///
    /// <para><b>종전엔 여기서 <c>Dispose</c>를 불렀다.</b> 그런데 <c>_held</c>에 들어가는 것은
    /// <b>정의상 "관리자가 아직 쥐고 있는 것"</b>이다 —
    /// 즉 <i>"못 걷은 것은 절대 놓아 주지 않는다"</i>는 규칙이
    /// <b>놓아 주는 함수에서 통째로 뒤집혀</b> 있었다.</para>
    ///
    /// <para>게다가 <c>DocumentToBeDestroyed</c>는 이름 그대로 <b>닫히기 전</b>이라
    /// 그 시점에 도면·뷰·임시 그래픽 관리자가 <b>아직 살아 있다</b>.
    /// 같은 핸들러가 <c>ClearAll</c>로 방금 <c>_held</c>에 넣은 것을 곧바로 지우기까지 했다.</para>
    ///
    /// <para>→ <b>아무것도 해제하지 않는다.</b> 소멸자는 <see cref="Hold"/>에서 이미 껐으므로
    /// 목록에서 빼는 것으로 충분하다(관리 객체 몇 개가 샌다 — 그것이 값이다).</para></summary>
    internal static void ReleaseFor(object doc)
    {
        int n = 0;
        for (int i = _held.Count - 1; i >= 0; i--)
            if (ReferenceEquals(_held[i].Doc, doc)) { _held.RemoveAt(i); n++; }
        if (n > 0) Note($"도면이 닫혀 붙잡아 둔 임시선 {n}개를 목록에서 뺐다(해제하지 않는다)");
    }

    /// <summary>표시가 실제로 그려졌는가 — 지표면처럼 <b>모양을 못 읽는 것</b>은 거짓이다.</summary>
    internal bool HasMarks => _marks.Count > 0;

    private PickMark() { }

    /// <summary>★ 고른 것의 모양을 베껴 <b>빨간 임시 선</b>으로 덧그린다.
    /// <para>못 그려도 <b>일을 막지 않는다</b> — 색은 거들 뿐이다. 다만 <b>조용히 넘어가지도 않는다</b>.</para></summary>
    public static PickMark Paint(Database db, ObjectId id)
        => Paint(null, db, id);

    /// <summary>★[검토 0909 · 보통] <b>도면을 받아서 적는다.</b>
    /// 종전엔 <c>MdiActiveDocument</c>를 적었는데, 부르는 쪽이 넘긴 <c>db</c>와 다른 순간이 오면
    /// 표시는 B 관리자에 그려지고 소유자는 B로 적히는데 <b>기록은 A에 담겨</b> 방어가 헛돈다.</summary>
    public static PickMark Paint(Autodesk.AutoCAD.ApplicationServices.Document doc, Database db, ObjectId id)
    {
        var m = new PickMark();
        if (db == null || id.IsNull) return m;
        try { m._doc = doc ?? AcadApp.DocumentManager.MdiActiveDocument; } catch { }
        try
        {
            List<Core.Point3> pts;
            bool closed;
            using (var tr = db.TransactionManager.StartTransaction())
            {
                // ★읽기만 한다 — 문서 잠금이 필요 없고, 뒤에 명령이 같은 객체를 쓰기로 여는 것과도 안 부딪힌다.
                pts = BoundaryReader.Read(tr, id);
                closed = IsClosed(tr, id);
                tr.Commit();
            }
            if (pts == null || pts.Count < 2)
            {
                Note($"고른 것에서 점을 {pts?.Count ?? 0}개밖에 못 읽어 표시를 안 했다");
                return m;
            }

            var v = new List<Point3d>(pts.Count + 1);
            foreach (var q in pts) v.Add(new Point3d(q.X, q.Y, q.Z));
            // ★★[검토 0908] <b>열린 것은 열린 채로 그린다.</b> 종전엔 무조건 첫 점을 다시 붙였는데,
            //   그러면 사용자가 <b>안 닫힌 계획선</b>을 골라도 표시는 닫혀 보여 <b>그 사실을 가린다</b>.
            //   있는 그대로 그리면 "내 계획선이 안 닫혔구나"를 눈으로 알려 줄 수 있다.
            if (closed && v.Count > 1 && v[0].DistanceTo(v[v.Count - 1]) > 1e-9) v.Add(v[0]);

            for (int i = 0; i + 1 < v.Count; i++)
            {
                // ★[검토 0908] 굵기는 <c>LWDISPLAY</c>가 꺼져 있으면 안 보인다 — <b>기대지 않는다</b>.
                //   눈에 띄는 것은 <b>빨강</b>이고 굵기는 덤이다.
                m._marks.Add(new Line(v[i], v[i + 1])
                {
                    ColorIndex = PickAci,
                    LineWeight = LineWeight.LineWeight070,
                });
            }

            // ★★★[JACK 0909 "줄이는 것보다 두껍게 하는 건 어때?"] <b>빨간 띠로 그린다.</b>
            //   굵게 그리면 흰 선 <b>양옆으로</b> 빨강이 삐져나와, 뒤에 있어도 보인다(<see cref="BandFrac"/>).
            double xMin = double.MaxValue, yMin = double.MaxValue, xMax = double.MinValue, yMax = double.MinValue;
            foreach (var q in v)
            {
                if (q.X < xMin) xMin = q.X;
                if (q.X > xMax) xMax = q.X;
                if (q.Y < yMin) yMin = q.Y;
                if (q.Y > yMax) yMax = q.Y;
            }
            double diag = System.Math.Sqrt((xMax - xMin) * (xMax - xMin) + (yMax - yMin) * (yMax - yMin));
            double half = diag * BandFrac * 0.5;
            if (!(half > 1e-9) || double.IsNaN(half)) half = BandMin * 0.5;

            // ★★[검토 0909 · 높음] <b>개수에 상한을 둔다.</b>
            //   동그라미만 솎고 띠는 안 솎았다. 그런데 <see cref="BoundaryReader"/>가 호를 <b>2m마다</b>
            //   잘게 나눈다 — 지름 500m 곡선 경계면 785점 → 임시선 <b>1,770개</b>,
            //   수치지도에서 온 3,000점 경계면 <b>6,200개</b>다.
            //   ★게다가 이 표시는 명령이 끝나도 안 사라진다(창이 들고 있는 것이 이 판의 설계라).
            //     도면을 옮겨 다닐 때마다 전부 걷고 전부 다시 만든다.
            //   굵은 띠라 몇 개 건너뛰어도 눈에는 똑같다.
            int segStep = (v.Count - 1) > MaxBands ? ((v.Count - 1) / MaxBands) + 1 : 1;
            for (int i = 0; i + 1 < v.Count; i += segStep)
            {
                Point3d a = v[i], b = v[System.Math.Min(i + segStep, v.Count - 1)];
                double dx = b.X - a.X, dy = b.Y - a.Y;
                double len = System.Math.Sqrt(dx * dx + dy * dy);
                if (len < 1e-9) continue;                     // 같은 점이 겹쳐 있다 — 띠를 못 만든다
                // 평면에서의 직각 방향 — Z는 그대로 두어 <b>3D 계획선도 제 높이에</b> 그려진다.
                double nx = -dy / len * half, ny = dx / len * half;
                // ★점 차례는 이 저장소의 선례를 따른다(SheetCommand: 좌하·우하·좌상·우상).
                //   순서를 틀리면 나비넥타이 모양으로 꼬인다.
                m._marks.Add(new Solid(
                    new Point3d(a.X - nx, a.Y - ny, a.Z), new Point3d(a.X + nx, a.Y + ny, a.Z),
                    new Point3d(b.X - nx, b.Y - ny, b.Z), new Point3d(b.X + nx, b.Y + ny, b.Z))
                { ColorIndex = PickAci });
            }

            // 모서리 메움 — 띠와 띠 사이에 생기는 쐐기 틈을 동그라미로 덮는다(둥근 모서리가 된다).
            //   ★꼭짓점이 아주 많으면(수치지도에서 온 선 등) 다 그리면 화면이 빨개진다 — 골라 그린다.
            int nV = v.Count;
            int step = nV > MaxDots ? (nV / MaxDots) + 1 : 1;
            for (int i = 0; i < nV; i += step)
                m._marks.Add(new Circle(v[i], Vector3d.ZAxis, half) { ColorIndex = PickAci });

            var tm = TransientManager.CurrentTransientManager;
            // ★★★[검토 0908] <b><c>Main</c>이라야 재생성을 견딘다.</b>
            //   첫 판은 <c>DirectShortTerm</c>이었는데 그것은 <b>끌기·지그</b>용(몇 백 밀리초)이고
            //   "재생성 때 다시 안 그린다". 이 표시는 명령이 도는 <b>수십 초</b> 살아야 하고
            //   그동안 사용자가 확대·이동·REGEN을 한다 — 중간에 사라지면 <b>아무도 못 알아챈다</b>
            //   (예외도 안 나고 <see cref="Paint"/>는 이미 성공을 돌려준 뒤다).
            var noIds = new IntegerCollection();
            // ★★★[검토 0909 · 높음] <b><c>AddTransient</c>도 <c>bool</c>을 돌려준다.</b>
            //   오늘 아침 <c>EraseTransient</c>에서 고친 것과 <b>똑같은 실수</b>를 다섯 줄 위에서 했다.
            //   안 들어갔는데 <c>_marks</c>에 남으면, 나중에 걷기가 실패해 <b>붙잡는 자리로 흘러간다</b>.
            //   ★안 들어간 것은 관리자가 <b>안 쥐고 있으므로</b> 그 자리에서 놓아 줘도 안전하다.
            int nAdd = 0;
            for (int i = m._marks.Count - 1; i >= 0; i--)
            {
                bool ok = false;
                // ★[JACK 0909] 순서값을 <b>최대</b>로. 이 값은 <b>임시 그래픽끼리의</b> 순서라
                //   흰 폴리선을 이기지는 못하지만, 우리가 정할 수 있는 것은 정해 둔다.
                try { ok = tm.AddTransient(m._marks[i], TransientDrawingMode.Main, 255, noIds); }
                catch { ok = false; }
                if (ok) nAdd++;
                else
                {
                    try { (m._marks[i] as Entity)?.Dispose(); } catch { }
                    m._marks.RemoveAt(i);
                }
            }
            if (nAdd == 0) Note("임시선을 하나도 못 걸었다 — 표시가 화면에 없다");
            Redraw();
        }
        catch (System.Exception ex)
        {
            // ★[검토 0909 · 낮음] 여기서 터졌으면 <b>아직 관리자에 안 넣은</b> 것이다 —
            //   그것을 <c>Clear()</c>로 보내면 걷기가 실패해 <b>괜히 붙잡는 목록만 늘어난다</b>.
            //   안 넣은 것은 그 자리에서 놓아 주는 것이 맞다.
            Note("표시 실패 — " + ex.Message);
            foreach (var d in m._marks) { try { (d as Entity)?.Dispose(); } catch { } }
            m._marks.Clear();
        }
        return m;
    }

    /// <summary>임시 선을 걷는다. <c>using</c>이 <b>정상 끝·예외·Esc</b> 어느 쪽에서도 부른다.
    /// <para>도면에는 아무것도 안 남겼으므로 여기서 못 걷어도 <b>도면은 멀쩡하다</b>.</para></summary>
    public void Dispose() => Clear();

    /// <summary>못 걷어서 <b>일부러 안 놓아 준</b> 개수 — 관리 객체 하나 새는 것이
    /// AutoCAD가 죽는 것보다 낫다. 세어 두지 않으면 <b>일어났는지조차 모른다</b>.</summary>
    internal static int LeakedCount => _leaked;
    private static int _leaked;

    private void Clear()
    {
        if (_marks.Count == 0) return;

        // ★★★[계획 3단계] <b>내 도면일 때만 걷는다.</b>
        //   다른 도면에서 걷으라고 시키면 그 도면의 관리자가 <b>죽은 포인터</b>를 쥔다(위 설명).
        //   ★<c>_doc</c>을 못 적었으면(옛 경로) 종전대로 걷는다 — 그때는 명령 안에서 바로 걷던 시절이다.
        try
        {
            var now = AcadApp.DocumentManager.MdiActiveDocument;
            if (_doc != null && !ReferenceEquals(_doc, now))
            {
                // ★★★[검토 0909 · 치명] <b>진짜로 붙잡는다.</b> 종전엔 여기서 <c>_marks.Clear()</c>였는데
                //   그것이 마지막 참조를 버리는 것이라, GC 소멸자가 네이티브를 지워
                //   <b>막으려던 죽은 포인터를 스스로 만들었다</b>(<see cref="_held"/> 참고).
                _stranded += _marks.Count;
                foreach (var d in _marks) Hold(_doc, d);
                _marks.Clear();
                return;
            }
        }
        catch { }

        try
        {
            var tm = TransientManager.CurrentTransientManager;
            var noIds = new IntegerCollection();
            foreach (var d in _marks)
            {
                // ★★★[검토 0908 · 높음] <b>걷은 것만 해제한다.</b>
                //   종전엔 걷기가 실패해도 무조건 해제했다 — 그러면 임시 그래픽 관리자가
                //   <b>이미 없는 객체를 가리키게</b> 되어 다음 화면 갱신에서 AutoCAD가 죽을 수 있다.
                //   관리 객체 하나가 새는 것이 <b>AutoCAD가 죽는 것보다 낫다</b>.
                //   ★★★[계획검토 0908 · 치명] <b>돌려주는 값을 실제로 봐야 한다.</b>
                //     <c>EraseTransient</c>는 <b><c>bool</c>을 돌려준다</b>. 못 걷어도
                //     <b>예외를 안 던지고 <c>false</c>만</b> 돌려준다 — 그런데 종전 코드는
                //     그 값을 버리고 "예외가 안 났으니 걷혔다"고 쳤다.
                //     ★그러면 <b>바로 위에 적어 둔 방어가 통째로 헛돈다</b> —
                //     못 걷은 것을 해제해 관리자가 죽은 포인터를 쥐게 된다.
                //     (오늘 아침 이 방어를 넣으면서 <b>걷혔는지를 안 본</b> 것이다.)
                bool erased = false;
                try { erased = tm.EraseTransient(d, noIds); }
                catch { erased = false; }
                if (erased) { try { (d as Entity)?.Dispose(); } catch { } }
                // ★[검토 0909 · 치명] 못 걷은 것은 <b>붙잡는다</b> — 놓아 주면 소멸자가 네이티브를 지운다.
                else { _leaked++; Hold(_doc, d); Note("임시선을 못 걷어 붙잡아 둔다"); }
            }
        }
        catch
        {
            // 관리자 자체를 못 얻었다 — 아무것도 해제하지 않고 <b>붙잡는다</b>(위와 같은 이유).
            foreach (var d in _marks) { _leaked++; Hold(_doc, d); }
        }
        _marks.Clear();
        Redraw();
    }

    /// <summary>고른 것이 <b>닫힌</b> 고리인가 — 있는 그대로 그리기 위해 묻는다.</summary>
    private static bool IsClosed(Transaction tr, ObjectId id)
    {
        try
        {
            object o = tr.GetObject(id, OpenMode.ForRead);
            if (o is Autodesk.AutoCAD.DatabaseServices.Polyline lw) return lw.Closed;
            if (o is Polyline3d p3) return p3.Closed;
            // 피처라인은 닫힘 속성이 없다 — <b>양 끝이 같은 자리면</b> 닫힌 것으로 본다.
            if (o is Autodesk.Civil.DatabaseServices.FeatureLine fl)
                try
                {
                    var pc = fl.GetPoints(Autodesk.Civil.FeatureLinePointType.AllPoints);
                    return pc != null && pc.Count > 2
                        && pc[0].DistanceTo(pc[pc.Count - 1]) < 1e-6;
                }
                catch { return false; }
        }
        catch { }
        return false;
    }

    /// <summary>표시가 안 나왔으면 <b>말한다</b> — 이 저장소는 조용한 실패를 금한다.</summary>
    private static void Note(string why)
    {
        try { DiagLog.Append("\n■ 선택 표시 — " + why); } catch { }
    }

    private static void Redraw()
    {
        try { AcadApp.DocumentManager.MdiActiveDocument?.Editor?.UpdateScreen(); } catch { }
    }
}
