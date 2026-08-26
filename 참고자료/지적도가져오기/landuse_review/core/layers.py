# -*- coding: utf-8 -*-
"""플러그인이 만든 캔버스 레이어의 **소유 표시**(F2).

`선택필지`·`인접필지`·`용지경계`·`편입구역` 을 **이름만으로** 지우면 사용자가 직접
만든 동명 레이어까지 삭제된다(미저장 데이터 손실). 그래서 플러그인이 만든 레이어에는
custom property 로 소유 마커를 달고, 삭제·교체는 **그 마커가 있는 레이어만** 대상으로 한다.

QGIS 타입에 의존하지 않도록 duck-typed(`setCustomProperty`/`customProperty`)로 쓴다 —
이 모듈은 qgis 를 import 하지 않으므로 어디서든 안전하게 불러올 수 있다.
"""

OWNED_PROP = "landuse_review/owned"


def mark_owned(layer):
    """플러그인 소유 마커를 단다. 삽입 직전에 호출."""
    if layer is not None:
        try:
            layer.setCustomProperty(OWNED_PROP, "1")
        except Exception:
            pass
    return layer


def is_owned(layer) -> bool:
    """플러그인이 만든(소유 마커가 달린) 레이어인가. 사용자 레이어는 마커가 없어 False."""
    try:
        return layer is not None and layer.customProperty(OWNED_PROP) == "1"
    except Exception:
        return False


def remove_owned(project, name: str):
    """같은 이름의 **플러그인 소유** 레이어만 제거한다. 사용자 동명 레이어는 두 채(F2)."""
    if project is None:
        return
    for old in list(project.mapLayersByName(name)):
        if is_owned(old):
            try:
                project.removeMapLayer(old.id())
            except Exception:
                pass


def replace_owned(project, name: str, new_layer):
    """소유 레이어만 제거하고 new_layer(있으면 소유 마킹 후) 추가한다.

    옛 `_replace_layer` 는 `mapLayersByName(name)` 전부를 지웠다 — 사용자가 만든 동명
    레이어까지. 편입 0(요구 8a) 처리가 `None` 으로 무조건 교체하도록 바뀌면서 이 위험이
    실제로 드러났다(Codex F2). 이제 소유 마커가 있는 것만 지운다.
    """
    if project is None:
        return
    remove_owned(project, name)
    if new_layer is not None:
        mark_owned(new_layer)
        project.addMapLayer(new_layer)
