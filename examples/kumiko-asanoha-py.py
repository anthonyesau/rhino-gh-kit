"""@component
{
  "name":        "Kumiko Asanoha PY",
  "nickname":    "Asanoha",
  "description": "The kumiko hemp-leaf pattern - a field of equilateral triangles, each latticed with spokes from its centroid to every vertex and edge midpoint, the classic asanoha of Japanese woodwork. Points are the triangle centroids; Curves are the triangle frames and spokes. Unwired inputs fall back to a 6-column panel of width 10.",
  "icon":        "icons/kumiko-asanoha-py.svg",
  "category":    "Vector",
  "subcategory": "Pattern",

  "inputs": [
    { "name": "Count", "nickname": "N", "type": "int", "access": "item",
      "description": "Triangle columns across the panel (default 6)." },
    { "name": "Scale", "nickname": "S", "type": "double", "access": "item",
      "description": "Overall panel width in model units (default 10)." },
    { "name": "BasePlane", "nickname": "P", "type": "Plane", "access": "item",
      "description": "Plane the panel lies on (default world XY)." },
    { "name": "Reach", "nickname": "R", "type": "double", "access": "item",
      "description": "Spoke length as a fraction of centroid-to-edge, 0 to 1 (default 1.0)." }
  ],

  "outputs": [
    { "name": "Points", "nickname": "P", "type": "Point3d", "access": "list",
      "description": "The centroid of every triangle." },
    { "name": "Curves", "nickname": "C", "type": "Curve", "access": "list",
      "description": "Triangle outlines plus six spokes per triangle." }
  ]
}
"""
# Shares Count / Scale / BasePlane -> Points / Curves with the other pattern
# scripts, so re-forging a component between them keeps those wires.
import math
import Rhino.Geometry as rg

cols = int(Count) if Count else 6
size = float(Scale) if Scale else 10.0
plane = BasePlane if BasePlane else rg.Plane.WorldXY
reach = float(Reach) if Reach is not None else 1.0

a = size / cols
h = a * math.sqrt(3.0) * 0.5
rows = cols
ox = -cols * a * 0.5
oy = -rows * h * 0.5

pts = []
crvs = []

def leaf(v1, v2, v3):
    gx = (v1[0] + v2[0] + v3[0]) / 3.0
    gy = (v1[1] + v2[1] + v3[1]) / 3.0
    g = plane.PointAt(gx, gy, 0.0)
    pts.append(g)
    corners = [plane.PointAt(x, y, 0.0) for x, y in (v1, v2, v3)]
    crvs.append(rg.PolylineCurve(corners + [corners[0]]))
    targets = list((v1, v2, v3))
    for p1, p2 in ((v1, v2), (v2, v3), (v3, v1)):
        targets.append(((p1[0] + p2[0]) * 0.5, (p1[1] + p2[1]) * 0.5))
    for tx, ty in targets:
        tip = plane.PointAt(gx + reach * (tx - gx), gy + reach * (ty - gy), 0.0)
        crvs.append(rg.LineCurve(g, tip))

for j in range(rows):
    y = oy + j * h
    off = 0.5 * a * (j % 2)
    for i in range(cols):
        x0 = ox + i * a + off
        leaf((x0, y), (x0 + a, y), (x0 + a * 0.5, y + h))          # upward
        if i < cols - 1:
            leaf((x0 + a, y), (x0 + a * 0.5, y + h), (x0 + a * 1.5, y + h))  # downward

Points = pts
Curves = crvs
