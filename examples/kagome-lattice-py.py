"""@component
{
  "name":        "Kagome Lattice PY",
  "nickname":    "Kagome",
  "description": "The kagome basket-weave lattice - hexagons meeting corner to corner so triangles open between them, drawn with a doubled outline so each lath reads as a woven strip. Points are the hexagon centers; Curves are the outer and inner hexagon outlines. Unwired inputs fall back to a 3-ring panel of width 10.",
  "icon":        "icons/kagome-lattice-py.svg",
  "category":    "Vector",
  "subcategory": "Pattern",

  "inputs": [
    { "name": "Count", "nickname": "N", "type": "int", "access": "item",
      "description": "Rings of hexagons around the center hexagon (default 3)." },
    { "name": "Scale", "nickname": "S", "type": "double", "access": "item",
      "description": "Overall panel width in model units (default 10)." },
    { "name": "BasePlane", "nickname": "P", "type": "Plane", "access": "item",
      "description": "Plane the panel lies on (default world XY)." },
    { "name": "Strip", "nickname": "W", "type": "double", "access": "item",
      "description": "Inner outline as a fraction of the hexagon, 0 to 1 (default 0.8)." }
  ],

  "outputs": [
    { "name": "Points", "nickname": "P", "type": "Point3d", "access": "list",
      "description": "The center point of every hexagon." },
    { "name": "Curves", "nickname": "C", "type": "Curve", "access": "list",
      "description": "Two concentric hexagon outlines per cell." }
  ]
}
"""
# Shares Count / Scale / BasePlane -> Points / Curves with the other pattern
# scripts, so re-forging a component between them keeps those wires.
import math
import Rhino.Geometry as rg

rings = int(Count) if Count else 3
size = float(Scale) if Scale else 10.0
plane = BasePlane if BasePlane else rg.Plane.WorldXY
strip = float(Strip) if Strip else 0.8

s = (size * 0.5) / (2.0 * rings + 1.0)  # hexagon circumradius; centers sit 2s apart
sqrt3 = math.sqrt(3.0)

def hexagon(cx, cy, radius):
    ring = []
    for k in range(6):
        a = math.radians(k * 60.0)  # vertex-at-0 orientation: corners meet corner to corner
        ring.append(plane.PointAt(cx + radius * math.cos(a), cy + radius * math.sin(a), 0.0))
    ring.append(ring[0])
    return rg.PolylineCurve(ring)

pts = []
crvs = []
for q in range(-rings, rings + 1):
    for r in range(max(-rings, -q - rings), min(rings, -q + rings) + 1):
        cx = 2.0 * s * (q + r * 0.5)
        cy = sqrt3 * s * r
        pts.append(plane.PointAt(cx, cy, 0.0))
        crvs.append(hexagon(cx, cy, s))
        crvs.append(hexagon(cx, cy, s * strip))

Points = pts
Curves = crvs
