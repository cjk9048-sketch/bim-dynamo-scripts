# -*- coding: utf-8 -*-
"""ReviewController — 위자드 액션 오케스트레이터.

ReviewWizard 의 액션 시그널을 받아 core 모듈을 호출하고, **결과는 모두 QGIS 캔버스에
레이어로 추가**한다. 각 step 페이지의 set_result() 로 요약만 표시(캔버스 중심 — 메모리 피드백).

레이어 4종 (캔버스 등록 이름):
  - 선택필지            (Step1)  · owner_name 칸은 설계자가 속성테이블에서 직접 입력
  - 인접필지            (Step1 옵션, 사용자 입력 반경 보조 시각화)
  - 용지경계            (Step2, 5186 정합)
  - 편입구역            (Step3)
"""
from qgis.PyQt.QtCore import QObject
from qgis.PyQt.QtWidgets import QFileDialog, QMessageBox

try:
    from qgis.core import QgsProject, QgsMessageLog, Qgis
    _HAS_QGIS_CORE = True
except ImportError:
    _HAS_QGIS_CORE = False

from ..ui.wizard_dialog import ReviewWizard
from ..api import vworld_fallback
from . import (
    parcel_lookup,
    owner_collector,
    boundary_input,
    area_calculator,
    inset_renderer,
    report_exporter,
    layers,
    background,
    coords,
)


class ReviewController(QObject):
    LAYER_PARCEL = "선택필지"
    LAYER_NEIGHBOR = "인접필지"
    LAYER_BOUNDARY = "용지경계"
    LAYER_INCLUSION = "편입구역"

    def __init__(self, iface):
        super().__init__()
        self.iface = iface
        self.canvas = iface.mapCanvas() if iface else None
        self.wizard = ReviewWizard(iface)
        self.shared = self.wizard.shared_data
        self._connect()

    def _connect(self):
        self.wizard.search_requested.connect(self._on_search)
        self.wizard.parcel_add_requested.connect(self._on_add_parcel)
        self.wizard.bulk_parcel_add_requested.connect(self._on_bulk_add_parcel)
        self.wizard.parcel_delete_requested.connect(self._on_delete_parcels)
        self.wizard.parcel_clear_requested.connect(self._on_clear_parcels)
        self.wizard.boundary_add_requested.connect(self._on_add_boundary)
        self.wizard.intersection_requested.connect(self._on_calc_intersection)
        self.wizard.inset_export_requested.connect(self._on_export_inset)
        self.wizard.report_export_requested.connect(self._on_export_report)
        self.wizard.wizard_reset.connect(self._on_reset)
        self.wizard.background_changed.connect(self._on_background_changed)

    def show_dialog(self):
        self.wizard.show()
        self.wizard.raise_()
        self.wizard.activateWindow()

    def _project(self):
        return QgsProject.instance() if _HAS_QGIS_CORE else None

    # ── Step1: 지번 검색 → 후보 → 캔버스 ──

    def _on_search(self):
        jibun = self.shared.get("jibun_text", "").strip()
        page1 = self.wizard.step_pages[0]
        if not jibun:
            self.wizard.report_step_result(0, "지번을 입력하세요.", "error")
            return
        try:
            cands = vworld_fallback.search_candidates(jibun)
        except Exception:
            cands = []
        self.shared["candidates"] = cands
        if hasattr(page1, "show_candidates"):
            page1.show_candidates(cands)

    def _on_add_parcel(self):
        jibun = self.shared.get("jibun_text", "").strip()
        radius_m = self.shared.get("radius_m", 0)
        if not jibun:
            self.wizard.report_step_result(0, "지번을 입력하세요.", "error")
            return
        try:
            cand = self.shared.get("selected_candidate")
            if cand and cand.get("pnu"):
                # 사용자가 고른 후보 — PNU+대표점으로 정확히 그 필지 취득
                new_rows = parcel_lookup.lookup_by_pnu(
                    cand["pnu"],
                    point=(cand.get("point_x"), cand.get("point_y")),
                    radius_m=radius_m,
                )
            else:
                # 후보 없음(키 없음·DB 폴백) — 입력 지번 직접 조회
                new_rows = parcel_lookup.lookup(jibun, radius_m=radius_m)

            n_added, n_dup, n_miss = self._apply_new_parcels(new_rows)

            if n_miss and not n_added:
                self.wizard.report_step_result(
                    0,
                    self._miss_reason(new_rows)
                    or f"'{jibun}' 필지를 찾지 못했습니다 — 지번을 확인하세요.",
                    "error")
                return
            level = "success" if n_added else "info"
            msg = f"선택필지 {n_added}건 추가" if n_added else "이미 추가된 필지입니다."
            if radius_m:
                n_neigh = sum(1 for p in new_rows if p.get("is_neighbor"))
                msg += f" + 인접 {n_neigh}건(반경 {radius_m}m)"
            msg += ".\n소유자 '이름'은 자동 조회 불가 → '선택필지' 속성테이블의 [소유자(직접입력)] 칸에 입력하세요."
            msg += self._truncation_note(new_rows)
            self.wizard.report_step_result(0, msg, level)
        except Exception as e:
            self.wizard.report_step_result(0, f"조회 실패: {e}", "error")

    @staticmethod
    def _miss_reason(rows):
        """미발견 행에 붙은 사유(동명 지역 등) 중 첫 번째. 없으면 빈 문자열."""
        for p in rows:
            if p.get("miss") and p.get("reason"):
                return p["reason"]
        return ""

    @staticmethod
    def _truncation_note(rows):
        """인접필지가 상한에서 잘렸으면 화면에 알린다(로그창에만 남기지 않는다)."""
        if any(p.get("neighbor_truncated") for p in rows):
            return (f"\n⚠ 인접필지가 {parcel_lookup.NEIGHBOR_LIMIT}건을 넘어 가까운 순으로 "
                    f"잘렸습니다. 반경을 줄이면 전부 볼 수 있습니다.")
        return ""

    def _on_bulk_add_parcel(self):
        """R2 — 여러 줄 지번을 한 번에 조회해 누적 목록에 병합한다."""
        bulk_text = (self.shared.get("bulk_jibun_text") or "").strip()
        if not bulk_text:
            self.wizard.report_step_result(0, "지번을 입력하세요.", "error")
            return
        radius_m = self.shared.get("radius_m", 0)
        try:
            new_rows = parcel_lookup.lookup(bulk_text, radius_m=radius_m)
        except Exception as e:
            self.wizard.report_step_result(0, f"일괄 조회 실패: {e}", "error")
            return
        n_added, n_dup, n_miss = self._apply_new_parcels(new_rows)
        msg = f"추가 {n_added}건 / 이미 있음 {n_dup}건 / 미발견 {n_miss}건"
        # 미발견 사유(동명 지역 등)는 줄마다 다를 수 있다 — 중복 없이 모아 보여준다.
        reasons = sorted({p["reason"] for p in new_rows if p.get("miss") and p.get("reason")})
        if reasons:
            msg += "\n" + "\n".join(reasons)
        msg += self._truncation_note(new_rows)
        level = "success" if n_added else ("info" if n_dup else "error")
        self.wizard.report_step_result(0, msg, level)

    def _on_delete_parcels(self):
        """R4 [선택 삭제] — 목록에서 고른 행을 누적 목록에서 제거(미발견 행 포함).

        pnu 가 아니라 parcel_key 로 지운다. 미발견 행은 pnu 가 전부 None 이라
        pnu 비교로는 한 건만 골라도 미발견 전체가 함께 지워진다.
        """
        keys = {tuple(k) for k in (self.shared.get("delete_keys") or [])}
        if not keys:
            return
        self.shared["parcels"] = [
            p for p in self.shared.get("parcels", []) if self._parcel_key(p) not in keys
        ]
        self._rebuild_canvas()
        self.wizard.report_step_result(0, f"선택 필지 {len(keys)}건 삭제.", "info")

    def _on_clear_parcels(self):
        """R4 [전체 비우기] — 누적된 검토 대상 필지를 모두 비운다."""
        self.shared["parcels"] = []
        self._rebuild_canvas()
        self.wizard.report_step_result(0, "검토 대상 필지를 모두 비웠습니다.", "info")

    # ── 다중 지번 누적 병합(R1) + 캔버스 재구성(R5) 공통 로직 ──

    @staticmethod
    def _parcel_key(p):
        """병합 키 — 정의는 parcel_lookup.parcel_key 한 곳(UI 삭제 키와 동일해야 함)."""
        return parcel_lookup.parcel_key(p)

    @classmethod
    def _merge_parcels(cls, existing, new_rows):
        """새 조회 결과를 누적 목록에 병합한다(덮어쓰기 아님, pnu 키).

        - 본 필지(is_neighbor=False)는 같은 pnu 의 기존 인접필지 행을 승격(교체)한다.
        - 인접필지 행은 같은 pnu 의 기존 본 필지 행을 절대 덮어쓰지 않는다.
        - 본 필지의 삽입 순서를 보존하고, 인접필지는 그 뒤에 오도록 정렬해 반환한다.
        """
        by_key = {cls._parcel_key(p): p for p in existing}
        order = [cls._parcel_key(p) for p in existing]

        for p in new_rows:
            k = cls._parcel_key(p)
            old = by_key.get(k)
            if old is None:
                by_key[k] = p
                order.append(k)
                continue
            old_is_neighbor = bool(old.get("is_neighbor"))
            new_is_neighbor = bool(p.get("is_neighbor"))
            if old_is_neighbor and not new_is_neighbor:
                by_key[k] = p        # 인접 → 본 승격
            elif not old_is_neighbor and new_is_neighbor:
                continue              # 본 필지는 인접행에 덮이지 않음
            else:
                by_key[k] = p          # 동일 유형 재조회 — 최신 데이터로 갱신(자리는 유지)

        merged = [by_key[k] for k in order]
        mains = [p for p in merged if not p.get("is_neighbor")]
        neighbors = [p for p in merged if p.get("is_neighbor")]
        return mains + neighbors

    def _apply_new_parcels(self, new_rows):
        """새 조회 결과를 누적 병합 + 캔버스 재구성하고 (추가/이미있음/미발견) 건수를 반환한다."""
        existing_main_keys = {
            self._parcel_key(p) for p in self.shared.get("parcels", [])
            if not p.get("is_neighbor")
        }
        self.shared["parcels"] = self._merge_parcels(self.shared.get("parcels", []), new_rows)
        self._rebuild_canvas()

        n_added = n_dup = n_miss = 0
        for p in new_rows:
            if p.get("is_neighbor"):
                continue
            if p.get("miss"):
                n_miss += 1
            elif self._parcel_key(p) in existing_main_keys:
                n_dup += 1
            else:
                n_added += 1
        return n_added, n_dup, n_miss

    def _rebuild_canvas(self):
        """R5 — 누적 목록으로 캔버스를 다시 그린다: 소유구분 1회 배치 재조회 + 수동입력 성명 보존.

        필지가 바뀌면 이전 편입 산출은 무효다 → 재산출 없이 4단계로 가면 현재 필지에 과거
        경계의 편입면적·편입률이 섞인다(F4). 마지막에 intersections 를 무효화한다.
        """
        parcels = self.shared.get("parcels", [])
        manual_names = self._owner_names_from_layer()   # R6 — 레이어 교체 전에 기존 값 수확
        try:
            owners = owner_collector.collect([p.get("pnu") for p in parcels])  # 배치 1회(루프 금지)
        except Exception as e:
            # 소유구분·공부면적(al_d160) 조회 실패 → book_area 미부착이면 검토서 공부면적이
            # 조용히 지적도 도형면적으로 대체된다(F5). 빈 목록으로 진행하되 반드시 알린다.
            owners = []
            self._warn(f"소유구분·공부면적을 불러오지 못했습니다({e}). 검토서 공부면적이 "
                       f"토지대장이 아닌 지적도 도형면적으로 대체될 수 있습니다.",
                       "민원검토 도구 — 공부면적")
        self._attach_book_areas(parcels, owners)
        radius_m = self.shared.get("radius_m", 0)
        parcel_layer, neighbor_layer = parcel_lookup.add_to_canvas(
            parcels, owners, project=self._project(),
            main_layer_name=self.LAYER_PARCEL,
            neighbor_layer_name=self.LAYER_NEIGHBOR,
            radius_m=radius_m,
            manual_names=manual_names,
        )
        self.shared["owners"] = owners
        self.shared["parcel_layer"] = parcel_layer
        self.shared["neighbor_layer"] = neighbor_layer
        if self.canvas and parcel_layer:
            ext = parcel_layer.extent()
            if not ext.isEmpty():
                self.canvas.setExtent(ext.buffered(ext.width() * 0.2 or 50))
                self.canvas.refresh()
        page1 = self.wizard.step_pages[0]
        if hasattr(page1, "refresh_parcel_list"):
            page1.refresh_parcel_list(parcels)
        self._invalidate_intersections()   # F4 — 필지 변경 → 이전 편입 산출 무효

    def _invalidate_intersections(self):
        """필지·경계가 바뀌면 이전 편입 산출을 무효화한다(F4).

        재산출 없이 4단계로 가면 현재 필지에 과거 경계의 편입면적·편입률이 섞여 인쇄된다
        (`hwpx_report.build_rows` 가 parcels[현재]와 intersections[과거]를 PNU 로 조인).
        '편입구역' 레이어를 지우고 status 를 비워, 3단계 게이트(`step3.execute_step`)가
        재산출을 요구하게 한다.
        """
        self.shared["intersections"] = []
        self.shared["inclusion_layer"] = None
        self.shared["intersect_status"] = None
        layers.remove_owned(self._project(), self.LAYER_INCLUSION)
        try:
            step3 = self.wizard.step_pages[2]
            if hasattr(step3, "reset"):
                step3.reset()
        except Exception:
            pass

    @staticmethod
    def _attach_book_areas(parcels, owners):
        """토지대장 면적(al_d160.a22)을 각 필지에 붙인다 — 편입률 분모·검토서 공부면적.

        연속지적도 도형면적(`area_sqm`)은 대장면적과 다르다(645-1: 4,670.7 vs 4,669.5).
        공식 검토서가 쓰는 값은 대장면적이므로 그쪽을 싣는다. 대장에 없는 필지는
        0 으로 남겨 두면 `parcel_lookup.book_area()` 가 도형면적으로 되돌린다.
        """
        book_by_pnu = {o.get("pnu"): float(o.get("area_sqm") or 0) for o in (owners or [])}
        for p in parcels:
            p["book_area"] = book_by_pnu.get(p.get("pnu"), 0.0)

    # ── 배경지도(개발 건 #2) ──

    def _canvas_extent_5186(self):
        """현재 캔버스 범위를 EPSG:5186 bbox 로. 지적 배경 조회·좌표계 추천 기준."""
        if self.canvas is None or not _HAS_QGIS_CORE:
            return None
        try:
            from qgis.core import QgsRectangle, QgsCoordinateReferenceSystem, QgsCoordinateTransform
            rect = QgsRectangle(self.canvas.extent())
            if rect.isEmpty():
                return None
            src = self.canvas.mapSettings().destinationCrs()
            tgt = QgsCoordinateReferenceSystem("EPSG:5186")
            if src and src.isValid() and src.authid() != tgt.authid():
                rect = QgsCoordinateTransform(src, tgt, QgsProject.instance()).transformBoundingBox(rect)
            return rect
        except Exception:
            return None

    def _on_background_changed(self, kind, on, opacity):
        """배경 바 토글/투명도 변경 → 배경 레이어 추가·투명도·제거."""
        project = self._project()
        if project is None:
            return
        try:
            if not on:
                background.remove_background(project, kind)
            elif background.set_background_opacity(project, kind, opacity):
                pass   # 이미 올라와 있으면 투명도만 조절(재조회 안 함)
            else:
                ext = self._canvas_extent_5186() if kind == "cadastral" else None
                ok = background.add_background(project, kind, opacity=opacity, extent=ext)
                if not ok:
                    msg = ("지적 배경을 불러오지 못했습니다(범위가 넓거나 조회 실패)."
                           if kind == "cadastral"
                           else "위성/수치지형 배경을 불러오지 못했습니다(V-World 키 필요).")
                    self._warn(msg, "민원검토 도구 — 배경지도")
            if self.canvas:
                self.canvas.refresh()
        except Exception as e:
            self._warn(f"배경지도 처리 실패: {e}", "민원검토 도구 — 배경지도")

    # ── Step2: 용지경계 → 캔버스 ──

    def _suggest_boundary_crs(self, path):
        """좌표계 미포함 파일(CAD 등)에 대해 기준(선택필지 5186 / 캔버스)과 겹치는 좌표계 추천.

        좌표 숫자만으로 belt 를 못 가리므로(false easting 200000 공유), 실제 필지 위치와
        겹치는 후보를 고른다. SHP 처럼 .prj 로 좌표계가 있으면 추천하지 않는다.
        """
        if not _HAS_QGIS_CORE:
            return None
        try:
            from qgis.core import QgsVectorLayer
            raw = QgsVectorLayer(path, "__crs_probe__", "ogr")
            if not raw.isValid() or raw.crs().isValid():
                return None   # 좌표계가 이미 있으면(SHP .prj 등) 추천 불필요
            ref = None
            pl = self.shared.get("parcel_layer")
            if pl is not None:
                ext = pl.extent()
                if not ext.isEmpty():
                    ref = ext
            if ref is None:
                ref = self._canvas_extent_5186()
            if ref is None:
                return None   # 기준이 없으면 추천 불가(선택필지·배경 먼저)
            sug = coords.suggest_crs_for_boundary(raw, ref)
            return sug[0][0] if sug else None
        except Exception:
            return None

    def _on_add_boundary(self):
        mode = self.shared.get("boundary_mode", "layer")
        assign_epsg = self.shared.get("boundary_assign_epsg")  # None or int(예 5186)
        try:
            if mode == "layer":
                inp = self.shared.get("boundary_input_layer")
                if inp is None:
                    self.wizard.report_step_result(1, "프로젝트의 폴리곤 레이어를 선택하세요.", "error")
                    return
                layer, warn = boundary_input.prepare(
                    inp, assign_epsg=assign_epsg,
                    target_name=self.LAYER_BOUNDARY, project=self._project(),
                )
            elif mode == "file":
                path, _ = QFileDialog.getOpenFileName(
                    self.wizard, "용지경계 파일 선택", "",
                    "경계 파일 (*.shp *.dxf);;Shapefile (*.shp);;CAD (*.dxf)",
                )
                if not path:
                    return
                # 좌표계 미지정 CAD 등이면 선택필지 위치와 겹치는 좌표계를 추천(개발 건 #2).
                suggested = None
                if assign_epsg is None:
                    suggested = self._suggest_boundary_crs(path)
                    if suggested:
                        assign_epsg = suggested
                layer, warn = boundary_input.load_from_file(
                    path, assign_epsg=assign_epsg,
                    target_name=self.LAYER_BOUNDARY, project=self._project(),
                )
                if layer is not None and suggested:
                    self.shared["boundary_layer"] = layer
                    self._invalidate_intersections()
                    self.wizard.report_step_result(
                        1,
                        f"용지경계 추가됨. 파일에 좌표계가 없어 EPSG:{suggested}로 추천·적용했습니다"
                        f"(선택필지 위치 기준). 배경지도를 켜서 위치가 맞는지 꼭 확인하세요.",
                        "info")
                    return
            else:  # draw
                layer = boundary_input.start_drawing(iface=self.iface, target_name=self.LAYER_BOUNDARY)
                warn = ""
                self.shared["boundary_layer"] = layer
                self._invalidate_intersections()   # F4 — 경계 변경 → 이전 편입 산출 무효
                self.wizard.report_step_result(1, "지도에서 폴리곤을 그리세요 (더블클릭으로 종료).", "info")
                return
            if layer is None:
                self.wizard.report_step_result(1, warn or "용지경계 추가 실패", "error")
                return
            self.shared["boundary_layer"] = layer
            self._invalidate_intersections()   # F4 — 경계 변경 → 이전 편입 산출 무효
            self.wizard.report_step_result(1, "용지경계 추가됨 (EPSG:5186 정합).", "success")
        except Exception as e:
            self.wizard.report_step_result(1, f"용지경계 추가 실패: {e}", "error")

    # ── Step3: 편입면적 → 캔버스 ──

    def _on_calc_intersection(self):
        if not self.shared.get("parcels"):
            self.wizard.report_step_result(2, "먼저 1단계 [선택필지]를 추가하세요.", "error")
            return
        if self.shared.get("boundary_layer") is None:
            self.wizard.report_step_result(2, "먼저 2단계 [용지경계]를 추가하세요.", "error")
            return
        try:
            rows, inclusion_layer, status = area_calculator.calculate_to_canvas(
                self.shared["parcels"], self.shared["boundary_layer"],
                target_name=self.LAYER_INCLUSION,
                parcel_layer=self.shared.get("parcel_layer"),
                project=self._project(),
            )
            self.shared["intersections"] = rows
            self.shared["inclusion_layer"] = inclusion_layer
            self.shared["intersect_status"] = status.code
            if self.canvas:
                self.canvas.refresh()
            # 공부면적이 토지대장이 아닌 도형면적으로 대체된 필지가 있으면 알린다(F5) —
            # 조용한 폴백은 검토서 '공부상 면적'이 대장과 다르게 나가도 사용자가 모른다.
            n_fb = sum(1 for r in rows if r.get("book_area_source") not in (None, "ledger"))
            msg = status.message
            if n_fb:
                msg += (f"\n⚠ {n_fb}개 필지는 토지대장 공부면적을 확인할 수 없어 지적도 "
                        f"도형면적으로 대체했습니다(검토서 '공부상 면적'이 대장과 다를 수 있음).")
            # 'no_inclusion'(편입 0)은 **정상 결과**다 — 조기 return 하지 않는다.
            # 4단계에서 삽도·검토서를 그대로 낸다(도로부 V3 요구 8a).
            self.wizard.report_step_result(2, msg, status.level)
        except Exception as e:
            self.wizard.report_step_result(2, f"산출 실패: {e}", "error")

    # ── Step4-1: 삽도 PNG ──

    def _on_export_inset(self):
        page4 = self.wizard.step_pages[3]
        bg = page4.get_background() if hasattr(page4, "get_background") else "satellite"
        extent_mode = page4.get_extent_mode() if hasattr(page4, "get_extent_mode") else "boundary"
        brightness = page4.get_brightness() if hasattr(page4, "get_brightness") else 60
        self.shared["inset_background"] = bg
        self.shared["inset_extent_mode"] = extent_mode
        self.shared["inset_brightness"] = brightness
        path, _ = QFileDialog.getSaveFileName(
            self.wizard, "삽도 PNG 저장", "민원검토_삽도.png", "PNG (*.png)"
        )
        if not path:
            return
        try:
            png = inset_renderer.render_to_png(
                self.iface, output_path=path, background=bg,
                parcel_layer=self.shared.get("parcel_layer"),
                boundary_layer=self.shared.get("boundary_layer"),
                inclusion_layer=self.shared.get("inclusion_layer"),
                extent_mode=extent_mode,
                brightness=brightness,
            )
            self.shared["inset_png_path"] = png
            self.wizard.report_step_result(3, f"삽도 저장 완료: {png}", "success")
        except Exception as e:
            self.wizard.report_step_result(3, f"삽도 생성 실패: {e}", "error")

    # ── Step4-2: HWPX ──

    def _on_export_report(self):
        if not self.shared.get("parcels"):
            self.wizard.report_step_result(3, "먼저 1단계 [선택필지]를 추가하세요.", "error")
            return
        path, _ = QFileDialog.getSaveFileName(
            self.wizard, "용지편입검토서 초안 저장", "용지편입검토서_초안.hwpx", "HWPX (*.hwpx)"
        )
        if not path:
            return
        page4 = self.wizard.step_pages[3]
        meta = page4.get_meta() if hasattr(page4, "get_meta") else {}
        mask = page4.get_mask() if hasattr(page4, "get_mask") else False
        owner_names = self._owner_names_from_layer()
        try:
            report_exporter.export(
                path,
                parcels=self.shared.get("parcels", []),
                intersections=self.shared.get("intersections", []),
                owner_names=owner_names,
                meta=meta,
                inset_png=self.shared.get("inset_png_path"),
                mask=mask,
            )
            self.shared["report_path"] = path
            engine = "도로부 양식" if report_exporter.has_template() else "기본 초안(양식 미수령)"
            self.wizard.report_step_result(3, f"검토서 저장 완료 [{engine}]: {path}", "success")
            QMessageBox.information(
                self.wizard, "검토서 초안 생성",
                "검토서 초안이 생성되었습니다.\n\n"
                "⚠ 자동 생성 초안입니다. 반드시 설계자가 한글에서 열어\n"
                "내용·삽도·소유자명을 확인한 후 결재하십시오.\n\n"
                f"{path}",
            )
        except Exception as e:
            self.wizard.report_step_result(3, f"검토서 생성 실패: {e}", "error")

    def _owner_names_from_layer(self):
        """'선택필지' 레이어 속성테이블에서 설계자가 입력한 owner_name 을 {PNU: 이름}으로 수집.

        성명은 이 도구에서 **유일하게 사람이 손으로 넣는 값**이다(어떤 API·DB 로도
        자동 조회되지 않는다). 조용히 잃으면 안 된다 — 옛 코드는 함수 전체를
        `except Exception: pass` 로 감싸 예외가 나면 수확된 성명이 통째로 사라지고
        다음 레이어 재구성에서 지워졌는데, 사용자는 아무 경고도 못 받았다(결함 D4).
        """
        out = {}
        layer = self.shared.get("parcel_layer")
        if layer is None:
            return out
        try:
            if not layer.isValid():
                return out
            # 속성테이블 편집을 저장(commit)하지 않은 상태면 getFeatures 가 직접 입력값을
            # 못 읽어 소유자명이 누락된다 → 읽기 전에 편집버퍼를 확정.
            if layer.isEditable() and not layer.commitChanges():
                self._warn_owner_names(
                    "'선택필지' 레이어의 편집 내용을 저장하지 못했습니다. "
                    "속성테이블에서 [저장]을 누른 뒤 다시 시도하세요.")
                return out
            fields = [f.name() for f in layer.fields()]
            if "pnu" not in fields or "owner_name" not in fields:
                return out
            for feat in layer.getFeatures():
                pnu = feat["pnu"]
                name = feat["owner_name"]
                if pnu and name:
                    out[str(pnu)] = str(name)
        except (RuntimeError, KeyError, AttributeError) as e:
            # RuntimeError = 삭제된 C++ 객체 접근. 여기서 조용히 {} 를 돌려주면
            # 그 뒤 레이어 재구성이 성명을 지운다 → 반드시 사용자에게 알린다.
            self._warn_owner_names(f"입력하신 소유자명을 읽지 못했습니다({e}). "
                                   f"레이어를 다시 그리기 전에 값을 확인하세요.")
        return out

    def _warn(self, msg: str, title: str = "민원검토 도구"):
        """로그 + messageBar 경고(불가 시 1단계 결과줄). 조용히 삼키지 않기 위한 공통 통로."""
        try:
            QgsMessageLog.logMessage(msg, "landuse_review", Qgis.Warning)
        except Exception:
            pass
        if self.iface is not None:
            try:
                self.iface.messageBar().pushWarning(title, msg)
                return
            except Exception:
                pass
        self.wizard.report_step_result(0, msg, "error")

    def _warn_owner_names(self, msg: str):
        self._warn(msg, "민원검토 도구 — 소유자명")

    # ── 초기화: 캔버스 레이어 제거 ──

    def _on_reset(self):
        vworld_fallback.clear_session_cache()
        if not _HAS_QGIS_CORE:
            return
        project = QgsProject.instance()
        # 소유 마커가 있는 레이어만 제거한다 — 사용자가 만든 동명 레이어 보호(F2).
        for name in (self.LAYER_PARCEL, self.LAYER_NEIGHBOR, self.LAYER_BOUNDARY, self.LAYER_INCLUSION):
            layers.remove_owned(project, name)
        background.remove_background(project)   # 배경지도 3종도 제거(개발 건 #2)
        # 배경 바 체크 해제(초기화 반영)
        try:
            for _kind, (cb, _sld) in getattr(self.wizard, "bg_controls", {}).items():
                cb.blockSignals(True); cb.setChecked(False); cb.blockSignals(False)
        except Exception:
            pass
        if self.canvas:
            self.canvas.refresh()

    def cleanup(self):
        vworld_fallback.clear_session_cache()
        try:
            for sig in (self.wizard.search_requested,
                        self.wizard.parcel_add_requested, self.wizard.bulk_parcel_add_requested,
                        self.wizard.parcel_delete_requested, self.wizard.parcel_clear_requested,
                        self.wizard.boundary_add_requested,
                        self.wizard.intersection_requested, self.wizard.inset_export_requested,
                        self.wizard.report_export_requested, self.wizard.wizard_reset):
                sig.disconnect()
        except Exception:
            pass
        if self.wizard:
            self.wizard.close()
