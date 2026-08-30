"""@component
{
  "name":        "Spirograph PY",
  "nickname":    "Spiro",
  "description": "A spirograph hypotrochoid - the path of a pen mounted off-center on a wheel rolling inside a ring, traced until it closes. Points are the pen samples; Curves is the single closed rosette. Unwired inputs fall back to a 2000-sample rosette of width 10.",
  "icon":        "icons/spirograph-py.svg",
  "category":    "Vector",
  "subcategory": "Pattern",

  "inputs": [
    { "name": "Count", "nickname": "N", "type": "int", "access": "item",
      "description": "Number of pen samples along the whole trace (default 2000)." },
    { "name": "Scale", "nickname": "S", "type": "double", "access": "item",
      "description": "The rosette is scaled to this overall width (default 10)." },
    { "name": "BasePlane", "nickname": "P", "type": "Plane", "access": "item",
      "description": "Plane the rosette lies on (default world XY)." },
    { "name": "Ratio", "nickname": "R", "type": "double", "access": "item",
      "description": "Wheel to ring size ratio, 0 to 1 - the lobe count comes from its reduced fraction (default 0.41)." },
    { "name": "Pen", "nickname": "D", "type": "double", "access": "item",
      "description": "Pen offset as a fraction of the wheel radius (default 0.8)." }
  ],

  "outputs": [
    { "name": "Points", "nickname": "P", "type": "Point3d", "access": "list",
      "description": "The pen position at every sample." },
    { "name": "Curves", "nickname": "C", "type": "Curve", "access": "list",
      "description": "The closed spirograph curve." }
  ]
}
"""
# Shares Count / Scale / BasePlane -> Points / Curves with the other pattern
# scripts, so re-forging a component between them keeps those wires.
import math
from fractions import Fraction
import Rhino.Geometry as rg

n = int(Count) if Count else 2000
size = float(Scale) if Scale else 10.0
plane = BasePlane if BasePlane else rg.Plane.WorldXY
ratio = float(Ratio) if Ratio else 0.41
pen = float(Pen) if Pen is not None else 0.8

frac = Fraction(max(0.02, min(0.98, ratio))).limit_denominator(30)
r, R = float(frac.numerator), float(frac.denominator)  # wheel r inside ring R
d = pen * r
t_end = 2.0 * math.pi * frac.numerator  # wheel turns until the trace closes
f = (size * 0.5) / ((R - r) + max(d, 1e-9))

pts = []
for i in range(n):
    t = t_end * i / n
    k = (R - r) / r
    x = (R - r) * math.cos(t) + d * math.cos(k * t)
    y = (R - r) * math.sin(t) - d * math.sin(k * t)
    pts.append(plane.PointAt(x * f, y * f, 0.0))

Points = pts
Curves = [rg.PolylineCurve(pts + [pts[0]])]
