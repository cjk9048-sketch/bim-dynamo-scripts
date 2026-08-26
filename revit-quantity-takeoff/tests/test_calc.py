"""calc.py 단위 테스트 — Revit 없이 순수 계산만 검증."""
import sys
import unittest
from pathlib import Path

sys.path.insert(0, str(Path(__file__).resolve().parents[1] / "scripts"))

from takeoff import calc
from takeoff.models import Member


def items_by_quantity(member):
    return {it.quantity: it for it in calc.compute(member)}


class ColumnTests(unittest.TestCase):
    def setUp(self):
        self.m = Member("Columns", mark="C1", type_name="RC400x600",
                        count=3, dims={"b": 0.4, "h": 0.6, "height": 3.0})

    def test_concrete(self):
        it = items_by_quantity(self.m)["concrete"]
        self.assertAlmostEqual(it.value, 2.16)
        self.assertEqual(it.unit, "m³")
        self.assertEqual(it.formula, "0.4×0.6×3×3")

    def test_formwork(self):
        it = items_by_quantity(self.m)["formwork"]
        self.assertAlmostEqual(it.value, 18.0)
        self.assertEqual(it.formula, "2×(0.4+0.6)×3×3")

    def test_count_and_length(self):
        items = items_by_quantity(self.m)
        self.assertEqual(items["count"].value, 3)
        self.assertEqual(items["count"].unit, "EA")
        self.assertAlmostEqual(items["length"].value, 9.0)
        self.assertEqual(items["length"].unit, "m")


class FramingTests(unittest.TestCase):
    def test_values(self):
        m = Member("StructuralFraming", mark="G1", count=2,
                   dims={"b": 0.3, "h": 0.5, "length": 6.0})
        items = items_by_quantity(m)
        self.assertAlmostEqual(items["concrete"].value, 1.8)
        self.assertAlmostEqual(items["formwork"].value, 15.6)
        self.assertAlmostEqual(items["length"].value, 12.0)


class FoundationTests(unittest.TestCase):
    def test_values(self):
        m = Member("Foundations", mark="F1", count=4,
                   dims={"b": 2.0, "l": 2.0, "h": 0.5})
        items = items_by_quantity(m)
        self.assertAlmostEqual(items["concrete"].value, 8.0)
        self.assertAlmostEqual(items["formwork"].value, 16.0)
        self.assertEqual(items["count"].value, 4)
        self.assertNotIn("length", items)  # 기초는 길이 산출 없음


class FloorTests(unittest.TestCase):
    def test_without_perimeter(self):
        m = Member("Floors", mark="S1", count=1,
                   dims={"area": 50.0, "thickness": 0.2})
        items = items_by_quantity(m)
        self.assertAlmostEqual(items["concrete"].value, 10.0)
        self.assertAlmostEqual(items["formwork"].value, 50.0)
        self.assertIn("하부면만", items["formwork"].note)

    def test_with_perimeter(self):
        m = Member("Floors", mark="S1", count=1,
                   dims={"area": 50.0, "thickness": 0.2, "perimeter": 30.0})
        items = items_by_quantity(m)
        self.assertAlmostEqual(items["formwork"].value, 56.0)


class WallTests(unittest.TestCase):
    def test_values(self):
        m = Member("Walls", mark="W1", count=2,
                   dims={"length": 5.0, "height": 3.0, "thickness": 0.2})
        items = items_by_quantity(m)
        self.assertAlmostEqual(items["concrete"].value, 6.0)
        self.assertAlmostEqual(items["formwork"].value, 60.0)
        self.assertAlmostEqual(items["length"].value, 10.0)


class OverlapDeductionTests(unittest.TestCase):
    def test_framing_deduct_length(self):
        # 양단 기둥에 0.4m씩 = 0.8m 겹침 공제
        m = Member("StructuralFraming", mark="G1", count=12,
                   dims={"b": 0.4, "h": 0.6, "length": 6.0, "deduct_length": 0.8})
        items = items_by_quantity(m)
        # 0.4*0.6*5.2*12 = 0.24*5.2*12 = 14.976
        self.assertAlmostEqual(items["concrete"].value, 14.976)
        # 산출식에는 순값(5.2)만, 겹침 표시는 없음
        self.assertEqual(items["concrete"].formula, "0.4×0.6×5.2×12")
        self.assertNotIn("-", items["concrete"].formula)
        self.assertIn("겹침", items["concrete"].note)  # 공제 내역은 비고로
        # 유효길이도 공제 반영
        self.assertAlmostEqual(items["length"].value, (6.0 - 0.8) * 12)

    def test_column_deduct_height(self):
        m = Member("Columns", mark="C1", count=2,
                   dims={"b": 0.5, "h": 0.5, "height": 3.6, "deduct_height": 0.6})
        items = items_by_quantity(m)
        # 0.5*0.5*(3.6-0.6)*2 = 0.25*3.0*2 = 1.5
        self.assertAlmostEqual(items["concrete"].value, 1.5)
        self.assertAlmostEqual(items["formwork"].value, 2 * (0.5 + 0.5) * 3.0 * 2)

    def test_no_deduction_is_backward_compatible(self):
        m = Member("StructuralFraming", mark="G2", count=1,
                   dims={"b": 0.4, "h": 0.6, "length": 6.0})
        it = items_by_quantity(m)["concrete"]
        self.assertEqual(it.formula, "0.4×0.6×6×1")  # 괄호 없이 그대로
        self.assertEqual(it.note, "")

    def test_over_deduction_raises(self):
        m = Member("StructuralFraming", mark="G3", count=1,
                   dims={"b": 0.4, "h": 0.6, "length": 0.5, "deduct_length": 0.8})
        with self.assertRaises(calc.CalcError):
            calc.compute(m)


class ErrorTests(unittest.TestCase):
    def test_unknown_category_raises(self):
        with self.assertRaises(ValueError):
            Member("Roof", dims={})

    def test_missing_dim_raises(self):
        m = Member("Columns", mark="C9", dims={"b": 0.4, "h": 0.6})  # height 누락
        with self.assertRaises(calc.CalcError):
            calc.compute(m)

    def test_non_numeric_dim_raises(self):
        m = Member("Columns", mark="C9", dims={"b": "x", "h": 0.6, "height": 3.0})
        with self.assertRaises(calc.CalcError):
            calc.compute(m)

    def test_quantities_filter(self):
        m = Member("Columns", mark="C1", count=1, dims={"b": 0.4, "h": 0.6, "height": 3.0})
        items = calc.compute(m, quantities={"concrete"})
        self.assertEqual([it.quantity for it in items], ["concrete"])


if __name__ == "__main__":
    unittest.main()
