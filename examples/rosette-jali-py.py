"""@component
{
  "name":        "Rosette Jali PY",
  "nickname":    "Rosette",
  "description": "A jali of twelve-fold star rosettes - a hexagonal field of rings, each woven with twelve chords that skip StarStep vertices, in the manner of Islamic star lattices. Points are the rosette centers; Curves are the rings and chords. Unwired inputs fall back to a 2-ring panel of width 10.",
  "icon":        "icons/rosette-jali-py.svg",
  "category":    "Vector",
  "subcategory": "Pattern",

  "inputs": [
    { "name": "Count", "nickname": "N", "type": "int", "access": "item",
      "description": "Rings of rosette cells around the center cell (default 2)." },
    { "name": "Scale", "nickname": "S", "type": "double", "access": "item",
      "description": "Overall panel width in model units (default 10)." },
    { "name": "BasePlane", "nickname": "P", "type": "Plane", "access": "item",
      "description": "Plane the panel lies on (default world XY)." },
    { "name": "StarStep", "nickname": "k", "type": "int", "access": "item",
      "description": "How many of the 12 ring vertices each chord skips, 1 to 11 (default 5)." }
  ],

  "outputs": [
    { "name": "Points", "nickname": "P", "type": "Point3d", "access": "list",
      "description": "The center point of every rosette." },
    { "name": "Curves", "nickname": "C", "type": "Curve", "access": "list",
      "description": "One circle plus twelve chords per rosette." }
  ]
}
"""
# Shares Count / Scale / BasePlane -> Points / Curves with the other pattern
# scripts, so re-forging a component between them keeps those wires.
import math
import Rhino.Geometry as rg

rings = int(Count) if Count else 2
size = float(Scale) if Scale else 10.0
plane = BasePlane if BasePlane else rg.Plane.WorldXY
step = int(StarStep) if StarStep else 5
step = max(1, min(11, step))

sqrt3 = math.sqrt(3.0)
s = (size * 0.5) / (sqrt3 * rings + 1.0)  # cell circumradius, panel spans Scale
r = s * 0.95

pts = []
crvs = []
for q in range(-rings, rings + 1):
    for rr in range(max(-rings, -q - rings), min(rings, -q + rings) + 1):
        cx = sqrt3 * s * (q + rr * 0.5)
        cy = 1.5 * s * rr
        center = plane.PointAt(cx, cy, 0.0)
        pts.append(center)

        frame = rg.Plane(plane)
        frame.Origin = center
        crvs.append(rg.Circle(frame, r).ToNurbsCurve())

        verts = []
        for k in range(12):
            a = math.radians(k * 30.0)
            verts.append(plane.PointAt(cx + r * math.cos(a), cy + r * math.sin(a), 0.0))
        for k in range(12):
            crvs.append(rg.LineCurve(verts[k], verts[(k + step) % 12]))

Points = pts
Curves = crvs
