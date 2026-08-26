# -*- coding: utf-8 -*-
"""지형 데이터 내보내기 — 타 CAD(Civil 3D 등) 연계용

무엇을 하나
    작업 범위의 등고선을 **표고가 Z 좌표에 들어간 3D 선형**으로 DXF 파일에 내보낸다.
    받는 쪽(Civil 3D)은 그 파일 하나로 지표면(TIN)을 만들 수 있다.

왜 이렇게 만들었나 — 셋 다 어기면 **오류 없이 잘못된 결과**가 나간다
  1) **Z 부여** — 운영DB 등고선(`public.contour`)은 2D 도형 + `cont`(등고수치) 속성 구조다.
     그대로 내보내면 CAD 는 표고를 모르는 평면 선만 받아 지표면을 만들지 못한다.
     → `ST_Force3D(geom, cont)` 로 표고를 Z 에 올린다.
  2) **도형 클립** — 등고선 한 행이 작업범위를 훨씬 넘어 길게 뻗어 있다. 행만 걸러 내보내면
     범위 밖 자료가 통째로 나간다(2km 범위 실측: 총연장 5,854km·정점 140만 →
     클립 후 203km·정점 5만, 파일 56MB → 2MB).
     → `ST_Intersection` 으로 **도형 자체를 자른다.**
  3) **좌표계 명시** — 파일에 좌표계가 없으면 CAD 에서 엉뚱한 위치에 놓이거나 축척이 틀어진다.
     운영DB 는 전 테이블 EPSG:5186 이므로 내보내기도 5186 고정이며, 받는 쪽에 반드시 알린다.

DXF 형식 제약 (실측으로 확인)
  · OGR DXF 드라이버는 **임의 속성을 만들지 못한다** → 내보내는 속성은 `Layer` 하나뿐.
    그 값이 DXF 레이어 이름이 되므로 주곡선/계곡선 분리에 활용한다.
  · 레이어 이름은 **ASCII 로 둔다** — DXF 는 코드페이지에 따라 한글이 깨질 수 있다.
"""

from qgis.core import (
    QgsCoordinateReferenceSystem,
    QgsCoordinateTransform,
    QgsDataSourceUri,
    QgsProject,
    QgsRasterFileWriter,
    QgsRasterLayer,
    QgsRasterPipe,
    QgsVectorFileWriter,
    QgsVectorLayer,
    QgsWkbTypes,
)

from .layer_loader import boundary_geometry_to_db_wkt
from ..db_env import DB_HOST, DB_PORT, DB_NAME, DB_USER, DB_PASSWORD, DB_SCHEMA

DB_SRID = 5186
DEM_TABLE = "korea_dem_90m"

# DXF 레이어 이름 — ASCII 고정(한글은 CAD 코드페이지에 따라 깨짐)
LAYER_MAIN = "CONTOUR_5M"          # 주곡선 (5m 간격)
LAYER_INDEX = "CONTOUR_INDEX_25M"  # 계곡선 (25m) — 브레이크라인 후보
DIVI_INDEX = "CTD001"              # 계곡선 구분 코드


class TerrainExportError(Exception):
    """내보내기 실패 — 메시지를 사용자에게 그대로 보여줄 수 있게 한글로 작성한다."""


def _safe_wkt(boundary_layer):
    """작업 범위 폴리곤을 EPSG:5186 WKT 로. 실패 시 사용자용 메시지와 함께 예외."""
    wkt = boundary_geometry_to_db_wkt(boundary_layer)
    if not wkt:
        raise TerrainExportError(
            "작업 범위를 읽지 못했습니다.\n"
            "'범위설정' 단계에서 범위를 먼저 지정해 주세요.\n"
            "(범위 레이어의 도형이 없거나 너무 많은 경우에도 발생합니다)"
        )
    # 쿼리 문자열에 직접 넣으므로 방어 (boundary_geometry_to_db_wkt 는 안전한 값을 주지만 이중 확인)
    if "'" in wkt or ";" in wkt:
        raise TerrainExportError("작업 범위 도형에 처리할 수 없는 문자가 포함되어 있습니다.")
    return wkt


def build_contour_layer(boundary_layer, simplify_m=0.0, layer_name="등고선_내보내기"):
    """작업 범위로 잘라낸 **3D 등고선** 레이어를 만든다(프로젝트에는 추가하지 않음).

    Args:
        boundary_layer: 작업 범위 폴리곤 레이어
        simplify_m: 0보다 크면 서버측에서 단순화(m). 정점 수를 줄이나 형상이 약간 뭉개진다.
                    수치지형도 1:5,000 의 도상 정밀도를 감안하면 1m 이하는 실질 손실이 없다.
        layer_name: 만들어질 레이어 이름

    Returns:
        QgsVectorLayer (MultiLineStringZ, EPSG:5186)
    """
    wkt = _safe_wkt(boundary_layer)
    b = "ST_GeomFromText('{w}', {s})".format(w=wkt, s=DB_SRID)

    # 2D 로 자른 뒤 Z 를 부여한다(등고선은 한 행의 표고가 상수라 순서를 바꿔도 값이 같다).
    clipped = "ST_CollectionExtract(ST_Intersection(c.geom, {b}), 2)".format(b=b)
    if simplify_m and simplify_m > 0:
        clipped = "ST_SimplifyPreserveTopology({g}, {t})".format(g=clipped, t=float(simplify_m))

    # elev(표고)는 SHP 등 속성을 실을 수 있는 형식에서 쓴다.
    # DXF 는 임의 속성을 못 만들어 "Layer" 만 내보내므로 여분 컬럼이 있어도 무해하다.
    sub = (
        "(SELECT _uid, \"Layer\", elev, geom FROM ("
        "  SELECT row_number() OVER () AS _uid,"
        "         CASE WHEN c.divi = '{di}' THEN '{li}' ELSE '{lm}' END AS \"Layer\","
        "         c.cont AS elev,"
        "         ST_Force3D({clip}, c.cont) AS geom"
        "  FROM {sch}.contour c"
        "  WHERE c.geom && {b} AND ST_Intersects(c.geom, {b})"
        ") q WHERE NOT ST_IsEmpty(q.geom))"
    ).format(di=DIVI_INDEX, li=LAYER_INDEX, lm=LAYER_MAIN, clip=clipped, sch=DB_SCHEMA, b=b)

    uri = QgsDataSourceUri()
    uri.setConnection(DB_HOST, str(DB_PORT), DB_NAME, DB_USER, DB_PASSWORD)
    uri.setDataSource("", sub, "geom", "", "_uid")
    uri.setSrid(str(DB_SRID))
    uri.setWkbType(QgsWkbTypes.MultiLineStringZ)

    layer = QgsVectorLayer(uri.uri(False), layer_name, "postgres")
    if not layer.isValid():
        raise TerrainExportError(
            "등고선을 불러오지 못했습니다.\n"
            "DB 연결 상태를 확인해 주세요.\n\n"
            "상세: {}".format(layer.error().summary()[:300])
        )
    return layer


# 기본 단순화 허용오차(m).
# DXF 쓰기 시간은 정점 수에 거의 비례하고, 등고선 원본에는 불필요한 점이 많다
# (정점 간격 평균 4.17m). 5km 범위 실측:
#     단순화 없음  329,367정점 / 19.47초 / 13.2MB
#     1.0m        82,348정점 /  6.06초 /  3.4MB   ← 총연장 변화 -0.5%
# 원본이 수치지형도 1:5,000(평면 정밀도 ±1~1.75m)이므로 1m 는 **원본이 보장하지 않는
# 정밀도 안쪽**이다 → 실질 손실 없이 3배 빠르고 4배 작다. 표고(Z)는 등고선마다 상수라 무영향.
DEFAULT_SIMPLIFY_M = 1.0


def export_contours_dxf(boundary_layer, out_path, simplify_m=DEFAULT_SIMPLIFY_M):
    """작업 범위 등고선을 3D DXF 로 내보낸다.

    Args:
        simplify_m: 단순화 허용오차(m). 0 이면 원본 정점 그대로(느림).

    Returns:
        dict — features / size_mb / path / crs / layers / simplify_m
    """
    layer = build_contour_layer(boundary_layer, simplify_m=simplify_m)

    count = layer.featureCount()
    if count == 0:
        raise TerrainExportError(
            "작업 범위 안에 등고선이 없습니다.\n"
            "범위가 너무 좁거나, 등고선이 없는 지역(해상 등)인지 확인해 주세요."
        )

    # DXF 드라이버는 임의 속성을 만들지 못한다 → "Layer" 만 내보낸다(= DXF 레이어 이름)
    idx = layer.fields().indexOf("Layer")
    opts = QgsVectorFileWriter.SaveVectorOptions()
    opts.driverName = "DXF"
    opts.fileEncoding = "UTF-8"
    opts.attributes = [idx] if idx >= 0 else []

    result = QgsVectorFileWriter.writeAsVectorFormatV3(
        layer, out_path, QgsProject.instance().transformContext(), opts
    )
    if result[0] != QgsVectorFileWriter.NoError:
        raise TerrainExportError(
            "DXF 저장에 실패했습니다.\n"
            "다른 프로그램이 같은 파일을 열고 있는지 확인해 주세요.\n\n"
            "상세: {}".format(str(result[1])[:300])
        )

    # 정점 수는 세지 않는다 — 세려면 DB 레이어를 **한 번 더 전수 조회**해야 하고
    # (5km 범위 실측 0.70초, 큰 범위에서는 더 커진다) 화면에 쓰지도 않는 값이었다.
    import os
    size_mb = os.path.getsize(out_path) / 1048576.0 if os.path.exists(out_path) else 0.0

    return {
        "path": out_path,
        "features": count,
        "size_mb": size_mb,
        "crs": "EPSG:{}".format(DB_SRID),
        "layers": [LAYER_MAIN, LAYER_INDEX],
        "simplify_m": simplify_m,
    }


def export_contours_shp(boundary_layer, out_path, simplify_m=0.0):
    """작업 범위 등고선을 3D Shapefile 로 내보낸다 (GIS 연계용).

    DXF 와의 차이 — 형식 특성에서 오는 것이라 기본값도 다르게 둔다:
      · **속성을 실을 수 있다** → `elev`(표고)·`Layer`(주곡선/계곡선) 함께 저장.
        DXF 는 임의 속성을 못 만들어 도면 레이어 이름만 남는다.
      · **이진 형식이라 빠르다** → 5km 범위 실측 0.39초(DXF 21초의 1/54).
        그래서 **단순화 기본값 0**(원본 정점 그대로) 이다. 굳이 형상을 깎을 이유가 없다.
      · 좌표계가 `.prj` 로 함께 나가고, `.cpg` 로 인코딩도 명시된다.

    Returns:
        dict — path / features / size_mb / crs / fields / simplify_m
    """
    layer = build_contour_layer(boundary_layer, simplify_m=simplify_m)

    count = layer.featureCount()
    if count == 0:
        raise TerrainExportError(
            "작업 범위 안에 등고선이 없습니다.\n"
            "범위가 너무 좁거나, 등고선이 없는 지역(해상 등)인지 확인해 주세요."
        )

    opts = QgsVectorFileWriter.SaveVectorOptions()
    opts.driverName = "ESRI Shapefile"
    opts.fileEncoding = "UTF-8"   # .cpg 로 함께 기록됨
    # 내부 키(_uid)는 내보내지 않고 의미 있는 속성만 남긴다
    keep = [layer.fields().indexOf(n) for n in ("Layer", "elev")]
    opts.attributes = [i for i in keep if i >= 0]

    result = QgsVectorFileWriter.writeAsVectorFormatV3(
        layer, out_path, QgsProject.instance().transformContext(), opts
    )
    if result[0] != QgsVectorFileWriter.NoError:
        raise TerrainExportError(
            "Shapefile 저장에 실패했습니다.\n"
            "다른 프로그램이 같은 파일을 열고 있는지 확인해 주세요.\n\n"
            "상세: {}".format(str(result[1])[:300])
        )

    import os
    total = 0
    base = os.path.splitext(out_path)[0]
    for ext in (".shp", ".shx", ".dbf", ".prj", ".cpg"):
        p = base + ext
        if os.path.exists(p):
            total += os.path.getsize(p)

    return {
        "path": out_path,
        "features": count,
        "size_mb": total / 1048576.0,
        "crs": "EPSG:{}".format(DB_SRID),
        "fields": ["Layer", "elev"],
        "simplify_m": simplify_m,
    }


# ──────────────────────────────────────────────────────────────
# DEM (90m 격자) 내보내기
# ──────────────────────────────────────────────────────────────

def _boundary_extent_5186(boundary_layer):
    """작업 범위의 bbox 를 EPSG:5186 QgsRectangle 로."""
    if boundary_layer is None or not boundary_layer.isValid():
        raise TerrainExportError(
            "작업 범위를 읽지 못했습니다.\n'범위설정' 단계에서 범위를 먼저 지정해 주세요."
        )
    rect = boundary_layer.extent()
    src = boundary_layer.crs()
    dst = QgsCoordinateReferenceSystem("EPSG:{}".format(DB_SRID))
    if src.isValid() and src != dst:
        tr = QgsCoordinateTransform(src, dst, QgsProject.instance())
        rect = tr.transformBoundingBox(rect)
    if rect.isEmpty():
        raise TerrainExportError("작업 범위가 비어 있습니다. 범위를 다시 지정해 주세요.")
    return rect


def build_dem_layer(boundary_layer, layer_name="DEM_내보내기"):
    """작업 범위에 걸치는 DEM 타일만 서버에서 골라 읽는 래스터 레이어.

    설계 메모 — 왜 임시 테이블을 만들지 않나:
        기존 로드 경로는 DB 에 임시 테이블(`temp_dem_*`)을 만들어 잘라 담는 방식인데,
        플러그인 계정(`waterviewer`)에 스키마 CREATE 권한이 없어 **실패하고 전국 DEM 로드로
        우회**하고 있다(11.3초). 여기서는 `where=` 조건으로 **서버에서 타일만 선별**한다
        — DB 쓰기 권한이 필요 없고 실측 0.9초. 상세는
        `docs/01_현황분석/gis_design_loader_DEM_결함분석_및_수정계획_v1.md`.

    ⚠ GDAL PostGISRaster 드라이버는 좌표계를 보고하지 않는다 → 명시적으로 지정한다.
       (지정하지 않으면 내보낸 파일이 좌표계 없이 나가 CAD 에서 위치가 어긋난다)
    """
    rect = _boundary_extent_5186(boundary_layer)
    env = "ST_MakeEnvelope({x0},{y0},{x1},{y1},{s})".format(
        x0=rect.xMinimum(), y0=rect.yMinimum(),
        x1=rect.xMaximum(), y1=rect.yMaximum(), s=DB_SRID)
    where = "ST_Intersects(rast, {})".format(env)

    uri = (
        "PG: dbname='{n}' host='{h}' port='{p}' user='{u}' password='{w}' "
        "schema='{s}' table='{t}' column='rast' mode=2 where='{q}'"
    ).format(n=DB_NAME, h=DB_HOST, p=DB_PORT, u=DB_USER, w=DB_PASSWORD,
             s=DB_SCHEMA, t=DEM_TABLE, q=where)

    layer = QgsRasterLayer(uri, layer_name, "gdal")
    if not layer.isValid():
        raise TerrainExportError(
            "DEM 을 불러오지 못했습니다.\nDB 연결 상태를 확인해 주세요.\n\n"
            "상세: {}".format(layer.error().summary()[:300])
        )
    layer.setCrs(QgsCoordinateReferenceSystem("EPSG:{}".format(DB_SRID)))
    return layer, rect


def export_dem_geotiff(boundary_layer, out_path):
    """작업 범위 DEM 을 GeoTIFF 로 내보낸다(좌표계 명시 포함).

    Returns:
        dict — path / width / height / pixel_m / size_mb / crs
    """
    layer, rect = build_dem_layer(boundary_layer)

    # 출력 격자를 **원본 DEM 격자에 맞춰 스냅**한다.
    # 요청 범위를 그대로 쓰면 픽셀 크기가 원본(90m)과 어긋나 표고가 재보간된다
    # (실측: 90.9m 로 출력되어 원본 값이 미세하게 바뀜). 바깥쪽으로 확장해 정렬한다.
    import math
    px = layer.rasterUnitsPerPixelX() or 90.0
    py = layer.rasterUnitsPerPixelY() or 90.0
    src = layer.extent()
    x0 = src.xMinimum() + math.floor((rect.xMinimum() - src.xMinimum()) / px) * px
    x1 = src.xMinimum() + math.ceil((rect.xMaximum() - src.xMinimum()) / px) * px
    y0 = src.yMinimum() + math.floor((rect.yMinimum() - src.yMinimum()) / py) * py
    y1 = src.yMinimum() + math.ceil((rect.yMaximum() - src.yMinimum()) / py) * py
    from qgis.core import QgsRectangle
    rect = QgsRectangle(x0, y0, x1, y1)
    cols = max(1, int(round(rect.width() / px)))
    rows = max(1, int(round(rect.height() / py)))

    crs = QgsCoordinateReferenceSystem("EPSG:{}".format(DB_SRID))
    writer = QgsRasterFileWriter(out_path)
    writer.setOutputFormat("GTiff")

    pipe = QgsRasterPipe()
    if not pipe.set(layer.dataProvider().clone()):
        raise TerrainExportError("DEM 내보내기 준비에 실패했습니다(데이터 파이프 구성 오류).")

    try:
        err = writer.writeRaster(pipe, cols, rows, rect, crs,
                                 QgsProject.instance().transformContext())
    except TypeError:
        # 구버전 QGIS 호환 (transformContext 인자 없음)
        err = writer.writeRaster(pipe, cols, rows, rect, crs)

    if err != QgsRasterFileWriter.NoError:
        raise TerrainExportError(
            "GeoTIFF 저장에 실패했습니다.\n"
            "다른 프로그램이 같은 파일을 열고 있는지 확인해 주세요.\n\n"
            "오류 코드: {}".format(err)
        )

    import os
    size_mb = os.path.getsize(out_path) / 1048576.0 if os.path.exists(out_path) else 0.0
    return {
        "path": out_path,
        "width": cols,
        "height": rows,
        "pixel_m": px,
        "size_mb": size_mb,
        "crs": "EPSG:{}".format(DB_SRID),
    }
