"""@component
{
  "name":        "Harmonograph PY",
  "nickname":    "Harmono",
  "description": "A three-pendulum harmonograph - damped sine motions on each axis weave a slowly collapsing ribbon of loops, lifted into 3D by a third pendulum on Z. Points are the pen samples; Curves is one polyline through them. Unwired inputs fall back to 4000 samples in a size-10 box.",
  "icon":        "icons/harmonograph-py.svg",
  "category":    "Vector",
  "subcategory": "Pattern",

  "inputs": [
    { "name": "Count", "nickname": "N", "type": "int", "access": "item",
      "description": "Number of pen samples along the trace (default 4000)." },
    { "name": "Scale", "nickname": "S", "type": "double", "access": "item",
      "description": "The trace is scaled to fit a box this wide (default 10)." },
    { "name": "BasePlane", "nickname": "P", "type": "Plane", "access": "item",
      "description": "Plane the trace is mapped onto (default world XY)." },
    { "name": "Freq", "nickname": "F", "type": "double", "access": "item",
      "description": "X pendulum frequency against the fixed Y frequency of 2 - near-rational values like 3.01 give slowly precessing loops (default 3.01)." },
    { "name": "Decay", "nickname": "D", "type": "double", "access": "item",
      "description": "Damping per time unit - higher collapses the drawing sooner (default 0.015)." }
  ],

  "outputs": [
    { "name": "Points", "nickname": "P", "type": "Point3d", "access": "list",
      "description": "The pen position at every sample, in time order." },
    { "name": "Curves", "nickname": "C", "type": "Curve", "access": "list",
      "description": "A single polyline through the trace." }
  ]
}
"""
# Shares Count / Scale / BasePlane -> Points / Curves with the other pattern
# scripts, so re-forging a component between them keeps those wires.
import math
import Rhino.Geometry as rg

n = int(Count) if Count else 4000
size = float(Scale) if Scale else 10.0
plane = BasePlane if BasePlane else rg.Plane.WorldXY
freq = float(Freq) if Freq else 3.01
decay = float(Decay) if Decay is not None else 0.015

T = 50.0
raw = []
for i in range(n):
    t = T * i / n
    e = math.exp(-decay * t)
    x = math.sin(freq * t + math.pi * 0.5) * e
    y = math.sin(2.0 * t) * e
    z = 0.35 * math.sin(5.0 * t) * math.exp(-decay * 1.5 * t)
    raw.append((x, y, z))

lo = [min(c[i] for c in raw) for i in range(3)]
hi = [max(c[i] for c in raw) for i in range(3)]
mid = [(lo[i] + hi[i]) * 0.5 for i in range(3)]
extent = max(hi[i] - lo[i] for i in range(3))
f = size / extent if extent > 0 else 1.0

pts = [plane.PointAt((c[0] - mid[0]) * f, (c[1] - mid[1]) * f, (c[2] - mid[2]) * f) for c in raw]

Points = pts
Curves = [rg.PolylineCurve(pts)]
