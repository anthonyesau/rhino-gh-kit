"""@component
{
  "name":        "Torus Knot PY",
  "nickname":    "TorusKnot",
  "description": "A torus knot - a closed curve winding WindP times around the axis while looping WindQ times through the hole of an invisible torus. Points are the curve samples; Curves is the closed knot. Unwired inputs fall back to a 2-5 knot of width 10.",
  "icon":        "icons/torus-knot-py.svg",
  "category":    "Vector",
  "subcategory": "Pattern",

  "inputs": [
    { "name": "Count", "nickname": "N", "type": "int", "access": "item",
      "description": "Number of samples along the knot (default 1200)." },
    { "name": "Scale", "nickname": "S", "type": "double", "access": "item",
      "description": "Overall knot width in model units (default 10)." },
    { "name": "BasePlane", "nickname": "P", "type": "Plane", "access": "item",
      "description": "Plane of the torus equator (default world XY)." },
    { "name": "WindP", "nickname": "p", "type": "int", "access": "item",
      "description": "Times the knot circles the torus axis (default 2)." },
    { "name": "WindQ", "nickname": "q", "type": "int", "access": "item",
      "description": "Times the knot loops through the torus hole (default 5)." }
  ],

  "outputs": [
    { "name": "Points", "nickname": "P", "type": "Point3d", "access": "list",
      "description": "The knot sample points, in parameter order." },
    { "name": "Curves", "nickname": "C", "type": "Curve", "access": "list",
      "description": "The closed knot polyline." }
  ]
}
"""
# Shares Count / Scale / BasePlane -> Points / Curves with the other pattern
# scripts, so re-forging a component between them keeps those wires.
import math
import Rhino.Geometry as rg

n = int(Count) if Count else 1200
size = float(Scale) if Scale else 10.0
plane = BasePlane if BasePlane else rg.Plane.WorldXY
p = int(WindP) if WindP else 2
q = int(WindQ) if WindQ else 5

R = 0.7 * size * 0.5  # torus center-circle radius
r = 0.3 * size * 0.5  # tube radius; R + r spans Scale

pts = []
for i in range(n):
    t = 2.0 * math.pi * i / n
    w = R + r * math.cos(q * t)
    pts.append(plane.PointAt(
        w * math.cos(p * t),
        w * math.sin(p * t),
        r * math.sin(q * t)))

Points = pts
Curves = [rg.PolylineCurve(pts + [pts[0]])]
