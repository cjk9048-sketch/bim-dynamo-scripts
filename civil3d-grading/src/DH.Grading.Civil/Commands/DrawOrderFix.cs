using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.Runtime;
using AcadEntity = Autodesk.AutoCAD.DatabaseServices.Entity;

namespace DH.Grading.Civil.Commands;

/// <summary>
/// [그리기 순서 — JACK 0731] 배경지도(위성 래스터) 위로 우리 문자가 보이게 정리한다.
///
/// 왜 필요한가:
///   AutoCAD는 "면으로 채워지는 것"(래스터 이미지·해치·트루타입 글자)끼리는 <b>그리기 순서</b>로
///   위아래가 정해지고, 단순한 선(폴리선)은 그 위에 그려진다. 그래서 배경지도를 깔면
///   <b>지적 선은 보이는데 지번 글자만 사진 밑에 깔려 안 보이는</b> 현상이 생긴다.
///   (AutoCAD의 TEXTTOFRONT 명령이 있는 이유와 같은 문제다.)
///
/// 해결: 이미지는 항상 맨 아래, 우리 문자는 항상 맨 위로 <b>커밋이 끝난 뒤 별도 트랜잭션</b>에서 정리한다.
///   (막 만든 객체를 같은 트랜잭션 안에서 옮기면 간헐적으로 반영되지 않는 사례가 있어 분리했다.)
/// 사용자가 직접 그린 문자는 건드리지 않는다 — 우리 레이어(DH-지번)만 대상.
/// </summary>
internal static class DrawOrderFix
{
    /// <summary>우리 레이어 문자는 맨 위, 래스터 이미지는 맨 아래로. 실패해도 무시(표시 문제일 뿐).</summary>
    internal static void Apply(Database db)
    {
        if (db == null) return;
        try
        {
            using var tr = db.TransactionManager.StartTransaction();
            var ms = (BlockTableRecord)tr.GetObject(
                SymbolUtilityServices.GetBlockModelSpaceId(db), OpenMode.ForWrite);

            // 지적도가 수만 개일 수 있으므로 **먼저 클래스로 걸러** 필요한 것만 연다(ObjectClass는 열지 않고 읽힌다).
            var clsImage = RXObject.GetClass(typeof(RasterImage));
            var clsText = RXObject.GetClass(typeof(DBText));
            var clsMText = RXObject.GetClass(typeof(MText));

            var images = new ObjectIdCollection();
            var texts = new ObjectIdCollection();
            foreach (ObjectId id in ms)      // 지워진 객체는 애초에 나오지 않는다
            {
                try
                {
                    RXClass c = id.ObjectClass;
                    if (c.IsDerivedFrom(clsImage)) { images.Add(id); continue; }
                    if (!c.IsDerivedFrom(clsText) && !c.IsDerivedFrom(clsMText)) continue;
                    if (tr.GetObject(id, OpenMode.ForRead) is AcadEntity e &&
                        string.Equals(e.Layer, ImportGisCommand.LayerJibun,
                                      System.StringComparison.OrdinalIgnoreCase))
                        texts.Add(id);
                }
                catch { }
            }

            if (images.Count == 0 && texts.Count == 0) { tr.Commit(); return; }

            var dot = (DrawOrderTable)tr.GetObject(ms.DrawOrderTableId, OpenMode.ForWrite);
            if (images.Count > 0) { try { dot.MoveToBottom(images); } catch { } }
            if (texts.Count > 0) { try { dot.MoveToTop(texts); } catch { } }
            tr.Commit();
        }
        catch { }
    }
}
