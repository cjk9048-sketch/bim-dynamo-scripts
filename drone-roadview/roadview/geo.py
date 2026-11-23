"""좌표 바꾸기 (WGS84 ↔ 도면 좌표계)."""
from __future__ import annotations

from pyproj import Transformer

from .pose import grid_convergence_deg


class Geo:
    def __init__(self, crs: str = 'EPSG:5186'):
        self.crs = crs
        self.fwd = Transformer.from_crs('EPSG:4326', crs, always_xy=True)
        self.inv = Transformer.from_crs(crs, 'EPSG:4326', always_xy=True)

    def ll_to_en(self, lat, lon):
        e, n = self.fwd.transform(lon, lat)
        return e, n

    def en_to_ll(self, e, n):
        lon, lat = self.inv.transform(e, n)
        return lat, lon

    def convergence(self, lat, lon):
        return grid_convergence_deg(self.fwd, lat, lon)
