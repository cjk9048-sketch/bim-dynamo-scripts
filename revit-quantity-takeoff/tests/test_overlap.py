"""overlap.resolve_overlaps 단위 테스트 — 겹침이 '한 번만' 귀속되는지 검증."""
import sys
import unittest
from pathlib import Path

sys.path.insert(0, str(Path(__file__).resolve().parents[1] / "scripts"))

from takeoff import calc, overlap
from takeoff.models import Member


class ResolveOverlapTests(unittest.TestCase):
    def make(self):
        # 0: 기둥 C1, 1: 보 G1 (보가 기둥에 물림)
        col = Member("Columns", mark="C1", count=1, dims={"b": 0.5, "h": 0.5, "height": 3.6})
        beam = Member("StructuralFraming", mark="G1", count=1, dims={"b": 0.4, "h": 0.7, "length": 6.0})
        return [col, beam]

    def test_beam_loses_to_column(self):
        members = self.make()
        res = overlap.resolve_overlaps(members, [{"a": 0, "b": 1, "length": 0.25}])
        self.assertEqual(res["applied"], 1)
        # 보(우선순위 낮음)에서 길이 공제, 기둥은 그대로
        self.assertAlmostEqual(members[1].dims["deduct_length"], 0.25)
        self.assertNotIn("deduct_height", members[0].dims)

    def test_priority_independent_of_input_order(self):
        # a,b 순서를 바꿔도 동일하게 보가 공제당함
        members = self.make()
        overlap.resolve_overlaps(members, [{"a": 1, "b": 0, "length": 0.25}])
        self.assertAlmostEqual(members[1].dims["deduct_length"], 0.25)

    def test_overlap_counted_once_total(self):
        # 보 양단이 두 기둥에 물림 -> 보만 2회 누적 공제, 기둥들은 변화 없음
        col1 = Member("Columns", mark="C1", count=1, dims={"b": 0.5, "h": 0.5, "height": 3.6})
        col2 = Member("Columns", mark="C2", count=1, dims={"b": 0.4, "h": 0.4, "height": 3.6})
        beam = Member("StructuralFraming", mark="G1", count=1, dims={"b": 0.4, "h": 0.7, "length": 6.0})
        members = [col1, col2, beam]
        overlap.resolve_overlaps(members, [
            {"a": 2, "b": 0, "length": 0.25},
            {"a": 2, "b": 1, "length": 0.20},
        ])
        self.assertAlmostEqual(beam.dims["deduct_length"], 0.45)
        self.assertNotIn("deduct_height", col1.dims)
        self.assertNotIn("deduct_height", col2.dims)

    def test_column_loses_to_foundation(self):
        found = Member("Foundations", mark="F1", count=1, dims={"b": 2.0, "l": 2.0, "h": 0.6})
        col = Member("Columns", mark="C1", count=1, dims={"b": 0.5, "h": 0.5, "height": 3.6})
        members = [found, col]
        overlap.resolve_overlaps(members, [{"a": 0, "b": 1, "length": 0.1}])
        # 기둥이 높이에서 공제, 기초는 그대로
        self.assertAlmostEqual(col.dims["deduct_height"], 0.1)
        self.assertNotIn("deduct_length", found.dims)

    def test_slab_has_no_linear_deduction(self):
        slab = Member("Floors", mark="S1", count=1, dims={"area": 50.0, "thickness": 0.2})
        beam = Member("StructuralFraming", mark="G1", count=1, dims={"b": 0.4, "h": 0.7, "length": 6.0})
        members = [slab, beam]
        # 보(우선순위 3) > 슬래브(4) 이므로 슬래브가 loser 인데 선형 공제 대상 아님 -> skipped
        res = overlap.resolve_overlaps(members, [{"a": 0, "b": 1, "length": 0.4}])
        self.assertEqual(res["applied"], 0)
        self.assertEqual(len(res["skipped"]), 1)

    def test_end_to_end_value_with_overlap(self):
        members = self.make()
        overlap.resolve_overlaps(members, [{"a": 0, "b": 1, "length": 0.5}])
        beam_items = {it.quantity: it for it in calc.compute(members[1])}
        # 0.4*0.7*(6-0.5)*1 = 0.28*5.5 = 1.54
        self.assertAlmostEqual(beam_items["concrete"].value, 1.54)


if __name__ == "__main__":
    unittest.main()
