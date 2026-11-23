"""Civil 3D 에서 저장한 DXF → 관로 선 (3D 폴리선 · 폴리선 · 선)."""
from __future__ import annotations

import fnmatch

import ezdxf
from ezdxf import colors as dxfcolors

_PALETTE = ['#00e5ff', '#ffd400', '#ff4fd8', '#7CFF4F', '#ff8a3d', '#b28cff', '#ff5a5a', '#4fa3ff']


def _layer_color(doc, name, i):
    try:
        lay = doc.layers.get(name)
        aci = abs(lay.color)
        if lay.has_dxf_attrib('true_color'):
            r, g, b = dxfcolors.int2rgb(lay.dxf.true_color)
        elif aci not in (7, 0, 256, 255) and 1 <= aci <= 255:
            r, g, b = dxfcolors.aci2rgb(aci)
        else:
            return _PALETTE[i % len(_PALETTE)]
        return '#%02x%02x%02x' % (r, g, b)
    except Exception:
        return _PALETTE[i % len(_PALETTE)]


def read_dxf(path: str, layer_patterns: list[str] | None = None):
    """→ (layers[{name,color}], lines[{layer, label, pts[[E,N,Z]...]}]). 좌표 = 도면 좌표 그대로."""
    doc = ezdxf.readfile(path)
    msp = doc.modelspace()
    raw = []
    for e in msp:
        t = e.dxftype()
        lay = e.dxf.layer
        if layer_patterns and not any(fnmatch.fnmatch(lay.upper(), p.upper()) for p in layer_patterns):
            continue
        pts = None
        if t == 'POLYLINE':
            if e.is_poly_face_mesh or e.is_polygon_mesh:
                continue
            if e.is_3d_polyline:
                pts = [tuple(v.dxf.location)[:3] for v in e.vertices]
            else:                                    # 2D 폴리선: 높이는 elevation 에, 좌표는 OCS → WCS
                ocs = e.ocs()
                z = e.dxf.elevation.z if e.dxf.hasattr('elevation') else 0.0
                pts = [tuple(ocs.to_wcs((v.dxf.location[0], v.dxf.location[1], z))) for v in e.vertices]
            if e.is_closed and pts:
                pts.append(pts[0])
        elif t == 'LWPOLYLINE':
            z = e.dxf.elevation if e.dxf.hasattr('elevation') else 0.0
            ocs = e.ocs()
            pts = [tuple(ocs.to_wcs((p[0], p[1], z))) for p in e.get_points('xy')]
            if e.closed and pts:
                pts.append(pts[0])
        elif t == 'LINE':
            pts = [tuple(e.dxf.start)[:3], tuple(e.dxf.end)[:3]]
        if pts and len(pts) >= 2:
            raw.append((lay, [list(map(float, p)) for p in pts]))
    names = []
    for lay, _ in raw:
        if lay not in names:
            names.append(lay)
    layers = [{'name': n, 'color': _layer_color(doc, n, i)} for i, n in enumerate(names)]
    lines = [{'layer': names.index(lay), 'label': lay, 'pts': pts} for lay, pts in raw]
    return layers, lines


def nearest_on_lines(lines, e, n):
    """(e,n) 에서 가장 가까운 관로 점 → (거리, z). 관로 없으면 (inf, None)."""
    best = (float('inf'), None)
    for ln in lines:
        p = ln['pts']
        for i in range(len(p) - 1):
            ax, ay, az = p[i]
            bx, by, bz = p[i + 1]
            dx, dy = bx - ax, by - ay
            L2 = dx * dx + dy * dy
            t = 0.0 if L2 == 0 else max(0.0, min(1.0, ((e - ax) * dx + (n - ay) * dy) / L2))
            px, py = ax + t * dx, ay + t * dy
            d = ((e - px) ** 2 + (n - py) ** 2) ** 0.5
            if d < best[0]:
                best = (d, az + t * (bz - az))
    return best
