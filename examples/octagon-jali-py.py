"""@component
{
  "name":        "Octagon Jali PY",
  "nickname":    "OctJali",
  "description": "A jali screen on the truncated-square tiling - a grid of octagons whose cut corners form tilted squares at every interior junction, the classic Mughal lattice. Points are the cell centers; Curves are the octagons and corner squares. Unwired inputs fall back to a 5 by 5 panel of width 10.",
  "icon":        "icons/octagon-jali-py.svg",
  "category":    "Vector",
  "subcategory": "Pattern",

  "inputs": [
    { "name": "Count", "nickname": "N", "type": "int", "access": "item",
      "description": "Octagon cells per side of the square panel (default 5)." },
    { "name": "Scale", "nickname": "S", "type": "double", "access": "item",
      "description": "Overall panel width in model units (default 10)." },
    { "name": "BasePlane", "nickname": "P", "type": "Plane", "access": "item",
      "description": "Plane the panel lies on (default world XY)." },
    { "name": "Cut", "nickname": "C", "type": "double", "access": "item",
      "description": "Corner cut as a fraction of the half cell, 0 to 1 (default 0.586 = regular octagons)." }
  ],

  "outputs": [
    { "name": "Points", "nickname": "P", "type": "Point3d", "access": "list",
      "description": "The center point of every octagon cell." },
    { "name": "Curves", "nickname": "C", "type": "Curve", "access": "list",
      "description": "Octagon outlines plus the tilted squares at interior junctions." }
  ]
}
"""
# Shares Count / Scale / BasePlane -> Points / Curves with the other pattern
# scripts, so re-forging a component between them keeps those wires.
import Rhino.Geometry as rg

n = int(Count) if Count else 5
size = float(Scale) if Scale else 10.0
plane = BasePlane if BasePlane else rg.Plane.WorldXY
cut = float(Cut) if Cut else 0.586

c = size / n
t = max(0.05, min(0.95, cut)) * c * 0.5

def closed(pts2d):
    pts = [plane.PointAt(x, y, 0.0) for x, y in pts2d]
    pts.append(pts[0])
    return rg.PolylineCurve(pts)

pts = []
crvs = []
for j in range(n):
    for i in range(n):
        cx = -size * 0.5 + (i + 0.5) * c
        cy = -size * 0.5 + (j + 0.5) * c
        pts.append(plane.PointAt(cx, cy, 0.0))
        h = c * 0.5
        crvs.append(closed([
            (cx - h + t, cy - h), (cx + h - t, cy - h),
            (cx + h, cy - h + t), (cx + h, cy + h - t),
            (cx + h - t, cy + h), (cx - h + t, cy + h),
            (cx - h, cy + h - t), (cx - h, cy - h + t)]))

# Tilted square at every interior four-cell junction.
for j in range(1, n):
    for i in range(1, n):
        jx = -size * 0.5 + i * c
        jy = -size * 0.5 + j * c
        crvs.append(closed([(jx + t, jy), (jx, jy + t), (jx - t, jy), (jx, jy - t)]))

Points = pts
Curves = crvs
