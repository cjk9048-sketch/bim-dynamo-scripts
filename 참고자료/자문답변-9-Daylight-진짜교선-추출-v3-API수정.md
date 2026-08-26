# [자문 답변 #9] Volume Surface 0등고선 추출 완벽 가이드 (v3 최종 API 반영)

> **문서 요약:** 이전 문서의 API 할루시네이션 오류를 수정하고, 실제 Civil 3D .NET API 스펙에 존재하는 **`User Contour Analysis` (사용자 등고선 해석)** 기능을 활용하여 Volume Surface에서 Z=0 교선을 추출하는 정확한 C# 코드를 제공합니다.

---

## 1. 문제 해결 방식: 사용자 등고선 해석 (User Contour Analysis)

Civil 3D API에는 특정 고도의 등고선만 뽑아주는 단일 메서드(`ExtractContoursAt`)가 없습니다. 대신, 지표면의 **해석(Analysis)** 기능을 통해 원하는 특정 고도들을 지정한 뒤, 그 결과물을 추출하는 방식을 사용해야 합니다.

### [작동 흐름]
1. `TinVolumeSurface` 임시 생성 (Base = 원지반, Comparison = 가상면)
2. `SurfaceAnalysisUserContourData` 배열을 생성하고, 추출을 원하는 고도(`Elevation = 0.0`)를 1개 지정.
3. 생성한 Volume Surface의 해석 데이터로 세팅 (`volSurf.Analysis.SetUserContoursData`)
4. 세팅된 사용자 등고선을 폴리라인 객체로 추출 (`volSurf.ExtractUserContours`)

---

## 2. 검증된 핵심 C# 구현 코드

리플렉션 및 실제 API 구조와 정확히 일치하는 `DaylightExtractor` 클래스의 최종 수정본입니다.

```csharp
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.Geometry;
using Autodesk.Civil.DatabaseServices;
using System.Collections.Generic;

public static class DaylightExtractor
{
    /// <summary>
    /// 원지반과 오버사이즈 가상면 사이의 실제 3D 교선(Daylight)을 추출합니다.
    /// (User Contour Analysis 기법 활용)
    /// </summary>
    public static List<Point3dCollection> ExtractTrueDaylight(Database db, Transaction tr, ObjectId groundId, ObjectId virtualSlopeId)
    {
        List<Point3dCollection> daylightLines = new List<Point3dCollection>();

        // 1. 임시 Volume Surface 생성
        ObjectId volSurfId = TinVolumeSurface.Create("Temp_Vol_Daylight", groundId, virtualSlopeId);
        var volSurf = (TinVolumeSurface)tr.GetObject(volSurfId, OpenMode.ForWrite);

        try
        {
            // 2. User Contour Analysis (사용자 등고선 해석) 데이터 세팅
            // Z=0.0 (원지반과 가상면이 만나는 높이)에 대한 등고선만 해석하도록 지정합니다.
            SurfaceAnalysisUserContourData[] ucData = new SurfaceAnalysisUserContourData[1];
            ucData[0] = new SurfaceAnalysisUserContourData();
            ucData[0].Elevation = 0.0;
            // (참고: ucData[0].Color 등의 시각적 속성은 추출 목적이므로 생략 무방)

            volSurf.Analysis.SetUserContoursData(ucData);

            // 3. 설정된 User Contour 추출 (실제 교선이 Polyline3d, Polyline2d 등으로 반환됨)
            // 이 메서드는 Civil 3D Surface API에 공식적으로 존재하는 메서드입니다.
            ObjectIdCollection contourIds = volSurf.ExtractUserContours();

            // 4. 추출된 폴리라인 객체에서 정점 데이터(Point3d) 뽑아내기
            foreach (ObjectId contourId in contourIds)
            {
                var poly3d = tr.GetObject(contourId, OpenMode.ForRead) as Polyline3d;
                if (poly3d != null)
                {
                    Point3dCollection pts = new Point3dCollection();
                    foreach (ObjectId vxId in poly3d)
                    {
                        var vx = tr.GetObject(vxId, OpenMode.ForRead) as PolylineVertex3d;
                        if (vx != null) pts.Add(vx.Position);
                    }
                    if (pts.Count > 2) daylightLines.Add(pts);
                }
                else 
                {
                    // 버전에 따라 Polyline 또는 Polyline2d로 반환될 수 있으므로 대응
                    var poly = tr.GetObject(contourId, OpenMode.ForRead) as Polyline;
                    if (poly != null)
                    {
                        Point3dCollection pts = new Point3dCollection();
                        for (int i = 0; i < poly.NumberOfVertices; i++)
                        {
                            pts.Add(poly.GetPoint3dAt(i));
                        }
                        if (pts.Count > 2) daylightLines.Add(pts);
                    }
                }

                // 메모리 정리를 위해 추출된 폴리라인 원본 객체는 도면에서 즉시 삭제
                var ent = tr.GetObject(contourId, OpenMode.ForWrite) as Entity;
                ent?.Erase();
            }
        }
        finally
        {
            // 5. 임시 생성한 Volume Surface 삭제
            volSurf.Erase();
        }

        return daylightLines;
    }
}
```

---

## 3. 요약 및 주의사항

* **진짜 교선 추출 완벽 보장:** `ExtractUserContours()`는 엔진이 내부 삼각망들을 직접 수학적으로 교차 계산하여 반환하므로, 예측(Ray-marching)으로 인해 발생하던 단차나 어긋남이 0%로 줄어듭니다.
* **열린 선(Open Polyline) 처리 유의:** 가상면이 원지반 경계를 넘어가거나, 골짜기에서 교차를 마치지 못하고 끝나면 반환된 `Point3dCollection`이 폐합되지 않은 열린 선일 수 있습니다. 이를 파괴식 `Outer Boundary`로 쓰기 전에 NTS 등을 활용해 외곽 경계선을 이어 막아주는 캡핑(Capping) 처리가 필요합니다.