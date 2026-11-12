using System.Collections.Generic;
using System.Linq;
using Autodesk.AutoCAD.EditorInput;
using Autodesk.AutoCAD.Geometry;
using Autodesk.AutoCAD.GraphicsInterface;
using DH.Grading.Core;

namespace DH.Grading.Civil;

/// <summary>★[v117.14 · JACK 1112 «옹벽 삭제 시 옹벽 추가로 만든 부분을 평면뷰에서 좀 보이게 해 줘 — 어디를 선택해야 할지 모르겠어 ·
/// 선택 마우스도 선 그리기 모양처럼 나오는데 선택 모양이 되어야 해»] 지울 옹벽 고르기(부지 [옹벽 삭제] · 진입도로 [옹벽 삭제] 함께).
/// <para>[옹벽 추가] 옹벽마다 옅은 주황 칠 + 테두리 + «옹벽 N» · 마우스가 올라간 옹벽은 빨강 · 커서 = 픽박스(EntitySelect).
/// 화면 없는 Civil은 <see cref="CursorLabel"/>이 그냥 찍기로 넘긴다(그림 없음 — 고르는 셈은 같음).</para></summary>
internal static class WallPickUi
{
    internal sealed class Cand
    {
        public int No;
        /// <summary>옹벽 자리 고리(닫힘 · 첫 점 반복 없음) — 없으면 <see cref="Line"/>만.</summary>
        public List<Point3> Ring = new();
        public List<Point3>? Line;
        /// <summary>«옹벽 N» 글을 이 조각에 쓸지(한 옹벽이 여러 조각이면 가장 큰 조각에만).</summary>
        public bool ShowLabel = true;
    }

    static TextStyle? _ts;
    static TextStyle Style(double h)
    {
        _ts ??= new TextStyle { Font = new FontDescriptor("Malgun Gothic", true, false, 129, 34) };
        try { _ts.TextSize = h; } catch { }
        return _ts;
    }

    static Point3d LabelAt(Cand w)
    {
        try
        {
            if (w.Ring.Count >= 3)
            {
                var gf = new NetTopologySuite.Geometries.GeometryFactory();
                var cs = w.Ring.Select(q => new NetTopologySuite.Geometries.Coordinate(q.X - w.Ring[0].X, q.Y - w.Ring[0].Y)).ToList();
                cs.Add(cs[0].Copy());
                var ip = gf.CreatePolygon(cs.ToArray()).Buffer(0).InteriorPoint;
                if (ip != null && !ip.IsEmpty) return new Point3d(ip.X + w.Ring[0].X, ip.Y + w.Ring[0].Y, 0);
            }
            if (w.Line is { Count: >= 2 } l) { var a = l[l.Count / 2 - (l.Count % 2 == 0 ? 1 : 0)]; var b = l[l.Count / 2]; return new Point3d((a.X + b.X) / 2, (a.Y + b.Y) / 2, 0); }
        }
        catch { }
        return w.Ring.Count > 0 ? new Point3d(w.Ring.Average(q => q.X), w.Ring.Average(q => q.Y), 0) : Point3d.Origin;
    }

    /// <summary>질문 하나 — 점(WCS 아님: <see cref="CursorLabel.Result.Value"/>는 UCS) · 키워드 · 취소. <paramref name="at"/> = 그 자리(WCS) 옹벽 번호(0 = 없음).</summary>
    internal static CursorLabel.Result Ask(Editor ed, PromptPointOptions ppo, string label, IReadOnlyList<Cand> walls, System.Func<double, double, int> at)
    {
        double h = 1.0;
        try { h = System.Math.Clamp(System.Convert.ToDouble(Autodesk.AutoCAD.ApplicationServices.Core.Application.GetSystemVariable("VIEWSIZE")) * 0.022, 0.2, 50); } catch { }
        var labels = walls.Where(w => w.ShowLabel).GroupBy(w => w.No).ToDictionary(g => g.Key, g => LabelAt(g.First()));
        void Draw(WorldDraw wd, Point3d cur, bool has)
        {
            int hot = 0;
            try { if (has) hot = at(cur.X, cur.Y); } catch { }
            foreach (var w in walls)
            {
                bool hit = w.No == hot;
                short aci = (short)(hit ? 1 : 30);
                if (w.Ring.Count >= 3)
                {
                    var pc = new Point3dCollection();
                    foreach (var q in w.Ring) pc.Add(new Point3d(q.X, q.Y, 0));
                    try
                    {
                        wd.SubEntityTraits.Color = aci;
                        wd.SubEntityTraits.FillType = FillType.FillAlways;
                        wd.SubEntityTraits.Transparency = new Autodesk.AutoCAD.Colors.Transparency((byte)(hit ? 150 : 95));
                        wd.Geometry.Polygon(pc);
                    }
                    catch { }
                    finally
                    {
                        try { wd.SubEntityTraits.FillType = FillType.FillNever; wd.SubEntityTraits.Transparency = new Autodesk.AutoCAD.Colors.Transparency((byte)255); } catch { }
                    }
                    wd.SubEntityTraits.Color = aci;
                    for (int i = 0; i < pc.Count; i++) wd.Geometry.WorldLine(pc[i], pc[(i + 1) % pc.Count]);
                }
                else if (w.Line is { Count: >= 2 } l)
                {
                    wd.SubEntityTraits.Color = aci;
                    for (int i = 0; i + 1 < l.Count; i++) wd.Geometry.WorldLine(new Point3d(l[i].X, l[i].Y, 0), new Point3d(l[i + 1].X, l[i + 1].Y, 0));
                }
                if (w.ShowLabel && labels.TryGetValue(w.No, out var lp))
                {
                    try
                    {
                        wd.SubEntityTraits.Color = aci;
                        wd.Geometry.Text(lp, Vector3d.ZAxis, Vector3d.XAxis, $"옹벽 {w.No}", true, Style(h));
                    }
                    catch { }
                }
            }
        }
        return CursorLabel.GetPoint(ed, ppo, label, "N 번호 · Esc 취소", Draw, null, CursorType.EntitySelect);
    }
}
