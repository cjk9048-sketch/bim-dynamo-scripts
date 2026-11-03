using Autodesk.AutoCAD.DatabaseServices;
using DH.Grading.Core;

namespace DH.Grading.Civil;

/// <summary>★★[v112.0 · 계획_도면화.md 1.5 · 4.9] <b>토공량 셈 — 면 찾기 + 두 면 세모로 직접 셈</b>(읽기만 · 임시 체적면을 안 만들어 도면을 안 바꾼다 — 1027 메모).
/// <para>[3D 보기](<c>InfraworksCommand.WriteVolumeCsv</c>)와 도면화 토공량표가 같이 쓴다 — 셈이 두 벌이면 한쪽만 고쳐진다(동작은 옛 그대로 옮김).</para></summary>
internal static class EarthworkVolume
{
    /// <summary>이름으로 면 둘(원지반 · 계획) — 같은 이름이 여럿이면 마지막(옛 <c>WriteVolumeCsv</c>와 같음) · 원지반 이름이 비면 못 찾음.</summary>
    internal static (ObjectId Ground, ObjectId Design) Find(Database db, string groundName, string designName)
    {
        ObjectId groundId = ObjectId.Null, designId = ObjectId.Null;
        using (Transaction tr = db.TransactionManager.StartTransaction())
        {
            var civilDoc = Autodesk.Civil.ApplicationServices.CivilDocument.GetCivilDocument(db);
            foreach (ObjectId sid in civilDoc.GetSurfaceIds())
            {
                if (tr.GetObject(sid, OpenMode.ForRead) is not Autodesk.Civil.DatabaseServices.TinSurface ts) continue;
                if (!string.IsNullOrEmpty(groundName) && ts.Name == groundName) groundId = sid;
                if (ts.Name == designName) designId = sid;
            }
            tr.Commit();
        }
        return (groundId, designId);
    }

    /// <summary>면의 보이는 세모(도면 좌표).</summary>
    internal static List<(Point3 A, Point3 B, Point3 C)> Tris(Database db, ObjectId surfId)
    {
        using Transaction tr = db.TransactionManager.StartTransaction();
        var tin = (Autodesk.Civil.DatabaseServices.TinSurface)tr.GetObject(surfId, OpenMode.ForRead);
        var l = new CachedGroundSurface(tin).AllTriangles().Select(t => (t.A, t.B, t.C)).ToList();
        tr.Commit();
        return l;
    }

    /// <summary>비교(계획) − 기준(원지반) 절토 · 성토 — <paramref name="mask"/>가 있으면 그 안(<paramref name="inside"/>)/밖만(Core <see cref="TinVolume"/>).</summary>
    internal static TinVolume.Result Compute(Database db, ObjectId designId, ObjectId groundId,
                                             NetTopologySuite.Geometries.Geometry? mask = null, bool inside = true)
        => TinVolume.Compute(Tris(db, designId), Tris(db, groundId), mask, inside);
}
