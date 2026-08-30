"""@component
{
  "name":          "Phyllo Dome PY",
  "nickname":      "Dome",
  "description":   "Sunflower phyllotaxis lifted onto a dome - Count seeds placed by the golden angle on a Vogel spiral, raised onto a paraboloid cap. Points are the seed centers; Curves are seed circles sized to the local packing, so the result reads like a sunflower head. Unwired inputs fall back to a 400-seed dome of radius 10.",
  "icon":          "icons/phyllo-dome-py.svg",
  "instanceGuid":  "cb2abe60-b5c1-4155-a4e2-d68f9e63b075",
  "category":      "Vector",
  "subcategory":   "Pattern",

  "inputs": [
    { "name": "Count", "nickname": "N", "type": "int", "access": "item",
      "description": "Number of seed points (default 400)." },
    { "name": "Scale", "nickname": "S", "type": "double", "access": "item",
      "description": "Dome radius in model units (default 10)." },
    { "name": "BasePlane", "nickname": "P", "type": "Plane", "access": "item",
      "description": "Plane the dome sits on (default world XY)." },
    { "name": "Lift", "nickname": "L", "type": "double", "access": "item",
      "description": "Dome height as a fraction of the radius (default 0.45, 0 = flat spiral)." }
  ],

  "outputs": [
    { "name": "Points", "nickname": "P", "type": "Point3d", "access": "list",
      "description": "Seed center points, in spiral order." },
    { "name": "Curves", "nickname": "C", "type": "Curve", "access": "list",
      "description": "One seed circle per point, sized to the local spacing." }
  ]
}
"""
# Wire-preservation demo trio (with Halvorsen Attractor PY and Hex Jali PY):
# all three share Count / Scale / BasePlane -> Points / Curves, so re-forging
# one component with a sibling script keeps those wires. Only the unique
# fourth input (here: Lift) differs.
import math
import Rhino.Geometry as rg

n = int(Count) if Count else 400
radius = float(Scale) if Scale else 10.0
plane = BasePlane if BasePlane else rg.Plane.WorldXY
lift = float(Lift) if Lift is not None else 0.45

golden = math.pi * (3.0 - math.sqrt(5.0))  # ~137.5077 degrees
seed_r = 0.42 * radius / math.sqrt(n)      # about half the mean neighbor spacing

pts = []
crvs = []
for i in range(n):
    t = (i + 0.5) / n
    r = radius * math.sqrt(t)
    a = i * golden
    z = lift * radius * (1.0 - t)  # paraboloid cap, apex at the center
    pt = plane.PointAt(r * math.cos(a), r * math.sin(a), z)
    pts.append(pt)
    frame = rg.Plane(plane)
    frame.Origin = pt
    crvs.append(rg.Circle(frame, seed_r).ToNurbsCurve())

Points = pts
Curves = crvs
