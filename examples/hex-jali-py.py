"""@component
{
  "name":        "Hex Jali PY",
  "nickname":    "Jali",
  "description": "A hexagonal jali screen - a honeycomb of cells, each holding a six-pointed star woven from two interlocking triangles, in the manner of a carved lattice panel. Points are the cell centers; Curves are the hexagon frames and star triangles. Unwired inputs fall back to a 3-ring panel of width 10.",
  "icon":        "icons/hex-jali-py.svg",
  "category":    "Vector",
  "subcategory": "Pattern",
  "language":    "python",

  "inputs": [
    { "name": "Count", "nickname": "N", "type": "int", "access": "item",
      "description": "Rings of hexagonal cells around the center cell (default 3)." },
    { "name": "Scale", "nickname": "S", "type": "double", "access": "item",
      "description": "Overall panel width in model units (default 10)." },
    { "name": "BasePlane", "nickname": "P", "type": "Plane", "access": "item",
      "description": "Plane the panel lies on (default world XY)." },
    { "name": "Inset", "nickname": "I", "type": "double", "access": "item",
      "description": "Star size as a fraction of the cell, 0 to 1 (default 0.82)." }
  ],

  "outputs": [
    { "name": "Points", "nickname": "P", "type": "Point3d", "access": "list",
      "description": "The center point of every cell." },
    { "name": "Curves", "nickname": "C", "type": "Curve", "access": "list",
      "description": "Hexagon frames and star triangles, cell by cell." }
  ]
}
"""
# Wire-preservation demo trio (with Phyllo Dome PY and Halvorsen Attractor PY):
# all three share Count / Scale / BasePlane -> Points / Curves, so re-forging
# one component with a sibling script keeps those wires. Only the unique
# fourth input (here: Inset) differs.
import math
import Rhino.Geometry as rg

rings = int(Count) if Count else 3
size = float(Scale) if Scale else 10.0
plane = BasePlane if BasePlane else rg.Plane.WorldXY
inset = float(Inset) if Inset else 0.82

sqrt3 = math.sqrt(3.0)
s = (size * 0.5) / (sqrt3 * rings + 1.0)  # hexagon circumradius so the panel spans Scale

def ring_pts(cx, cy, radius, angles):
    ring = []
    for a in angles:
        rad = math.radians(a)
        ring.append(plane.PointAt(cx + radius * math.cos(rad), cy + radius * math.sin(rad), 0.0))
    ring.append(ring[0])  # close the loop
    return ring

hex_angles = [90.0 + k * 60.0 for k in range(6)]  # pointy-top hexagon
tri_up = [90.0, 210.0, 330.0]
tri_down = [30.0, 150.0, 270.0]

pts = []
crvs = []
for q in range(-rings, rings + 1):
    for r in range(max(-rings, -q - rings), min(rings, -q + rings) + 1):
        cx = sqrt3 * s * (q + r * 0.5)
        cy = 1.5 * s * r
        pts.append(plane.PointAt(cx, cy, 0.0))
        crvs.append(rg.PolylineCurve(ring_pts(cx, cy, s, hex_angles)))
        crvs.append(rg.PolylineCurve(ring_pts(cx, cy, s * inset, tri_up)))
        crvs.append(rg.PolylineCurve(ring_pts(cx, cy, s * inset, tri_down)))

Points = pts
Curves = crvs
