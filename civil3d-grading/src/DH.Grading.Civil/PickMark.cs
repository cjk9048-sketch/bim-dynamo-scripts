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

    private readonly List<Drawable> _marks = new();

    private PickMark() { }

    /// <summary>★ 고른 것의 모양을 베껴 <b>빨간 임시 선</b>으로 덧그린다.
    /// <para>못 그려도 <b>일을 막지 않는다</b> — 색은 거들 뿐이다. 다만 <b>조용히 넘어가지도 않는다</b>.</para></summary>
    public static PickMark Paint(Database db, ObjectId id)
    {
        var m = new PickMark();
        if (db == null || id.IsNull) return m;
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

            var tm = TransientManager.CurrentTransientManager;
            // ★★★[검토 0908] <b><c>Main</c>이라야 재생성을 견딘다.</b>
            //   첫 판은 <c>DirectShortTerm</c>이었는데 그것은 <b>끌기·지그</b>용(몇 백 밀리초)이고
            //   "재생성 때 다시 안 그린다". 이 표시는 명령이 도는 <b>수십 초</b> 살아야 하고
            //   그동안 사용자가 확대·이동·REGEN을 한다 — 중간에 사라지면 <b>아무도 못 알아챈다</b>
            //   (예외도 안 나고 <see cref="Paint"/>는 이미 성공을 돌려준 뒤다).
            var noIds = new IntegerCollection();
            foreach (var d in m._marks) tm.AddTransient(d, TransientDrawingMode.Main, 128, noIds);
            Redraw();
        }
        catch (System.Exception ex) { Note("표시 실패 — " + ex.Message); m.Clear(); }
        return m;
    }

    /// <summary>임시 선을 걷는다. <c>using</c>이 <b>정상 끝·예외·Esc</b> 어느 쪽에서도 부른다.
    /// <para>도면에는 아무것도 안 남겼으므로 여기서 못 걷어도 <b>도면은 멀쩡하다</b>.</para></summary>
    public void Dispose() => Clear();

    private void Clear()
    {
        if (_marks.Count == 0) return;
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
                bool erased = false;
                try { tm.EraseTransient(d, noIds); erased = true; }
                catch { }
                if (erased) try { (d as Entity)?.Dispose(); } catch { }
            }
        }
        catch { }   // 관리자 자체를 못 얻었다 — 그때는 <b>아무것도 해제하지 않는다</b>(위와 같은 이유)
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
