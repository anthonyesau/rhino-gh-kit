"""@component
{
  "name":        "Plain Weave PY",
  "nickname":    "Weave",
  "description": "A plain over-under weave in 3D - Count warp and Count weft threads undulate through each other with true alternating crossings, like cloth seen up close. Points are the crossing sites; Curves are the threads. Unwired inputs fall back to a 12 by 12 weave of width 10.",
  "icon":        "icons/plain-weave-py.svg",
  "category":    "Vector",
  "subcategory": "Pattern",

  "inputs": [
    { "name": "Count", "nickname": "N", "type": "int", "access": "item",
      "description": "Threads in each direction (default 12)." },
    { "name": "Scale", "nickname": "S", "type": "double", "access": "item",
      "description": "Overall cloth width in model units (default 10)." },
    { "name": "BasePlane", "nickname": "P", "type": "Plane", "access": "item",
      "description": "Plane the cloth lies on (default world XY)." },
    { "name": "Amplitude", "nickname": "A", "type": "double", "access": "item",
      "description": "Thread lift at each crossing as a fraction of the thread spacing (default 0.35)." }
  ],

  "outputs": [
    { "name": "Points", "nickname": "P", "type": "Point3d", "access": "list",
      "description": "One point at every warp-weft crossing, on the cloth midplane." },
    { "name": "Curves", "nickname": "C", "type": "Curve", "access": "list",
      "description": "All warp threads, then all weft threads." }
  ]
}
"""
# Shares Count / Scale / BasePlane -> Points / Curves with the other pattern
# scripts, so re-forging a component between them keeps those wires.
import math
import Rhino.Geometry as rg

n = int(Count) if Count else 12
size = float(Scale) if Scale else 10.0
plane = BasePlane if BasePlane else rg.Plane.WorldXY
amp = float(Amplitude) if Amplitude is not None else 0.35

g = size / n
A = amp * g
m = 8 * n  # samples per thread

pts = []
crvs = []

# Warp: constant x per thread, z alternates so crossings go over-under.
for j in range(n):
    x = (j + 0.5) * g - size * 0.5
    thread = []
    for k in range(m + 1):
        u = n * k / m
        y = u * g - size * 0.5
        z = A * math.cos(math.pi * (u - 0.5 - j))
        thread.append(plane.PointAt(x, y, z))
    crvs.append(rg.PolylineCurve(thread))

# Weft: constant y per thread, opposite phase at every crossing.
for k in range(n):
    y = (k + 0.5) * g - size * 0.5
    thread = []
    for kk in range(m + 1):
        v = n * kk / m
        x = v * g - size * 0.5
        z = -A * math.cos(math.pi * (v - 0.5 - k))
        thread.append(plane.PointAt(x, y, z))
    crvs.append(rg.PolylineCurve(thread))

for j in range(n):
    for k in range(n):
        pts.append(plane.PointAt((j + 0.5) * g - size * 0.5, (k + 0.5) * g - size * 0.5, 0.0))

Points = pts
Curves = crvs
