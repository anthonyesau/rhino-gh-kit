"""@component
{
  "name":        "Guilloche Rings PY",
  "nickname":    "Guilloche",
  "description": "Guilloche engine turning - a nest of concentric rings, each modulated by a sine wave and twisted a little further than the last, like the engraved rosettes on banknotes and watch dials. Points mark each ring's twist phase; Curves are the modulated rings. Unwired inputs fall back to 24 rings of width 10.",
  "icon":        "icons/guilloche-rings-py.svg",
  "category":    "Vector",
  "subcategory": "Pattern",

  "inputs": [
    { "name": "Count", "nickname": "N", "type": "int", "access": "item",
      "description": "Number of concentric rings (default 24)." },
    { "name": "Scale", "nickname": "S", "type": "double", "access": "item",
      "description": "Overall rosette width in model units (default 10)." },
    { "name": "BasePlane", "nickname": "P", "type": "Plane", "access": "item",
      "description": "Plane the rosette lies on (default world XY)." },
    { "name": "Waves", "nickname": "W", "type": "int", "access": "item",
      "description": "Sine waves around each ring (default 12)." },
    { "name": "Twist", "nickname": "T", "type": "double", "access": "item",
      "description": "Phase shift between neighboring rings in radians (default 0.35)." }
  ],

  "outputs": [
    { "name": "Points", "nickname": "P", "type": "Point3d", "access": "list",
      "description": "One phase marker point per ring - together they trace the twist spiral." },
    { "name": "Curves", "nickname": "C", "type": "Curve", "access": "list",
      "description": "The modulated rings, innermost first." }
  ]
}
"""
# Shares Count / Scale / BasePlane -> Points / Curves with the other pattern
# scripts, so re-forging a component between them keeps those wires.
import math
import Rhino.Geometry as rg

n = int(Count) if Count else 24
size = float(Scale) if Scale else 10.0
plane = BasePlane if BasePlane else rg.Plane.WorldXY
waves = int(Waves) if Waves else 12
twist = float(Twist) if Twist is not None else 0.35

R = size * 0.5
inner = 0.25 * R
gap = (R - inner) / (n + 1)
amp = 0.45 * gap
samples = 240

pts = []
crvs = []
for i in range(n):
    base = inner + (i + 1) * gap
    phase = i * twist
    ring = []
    for k in range(samples + 1):
        t = 2.0 * math.pi * k / samples
        r = base + amp * math.sin(waves * t + phase)
        ring.append(plane.PointAt(r * math.cos(t), r * math.sin(t), 0.0))
    crvs.append(rg.PolylineCurve(ring))
    pts.append(plane.PointAt(base * math.cos(phase), base * math.sin(phase), 0.0))

Points = pts
Curves = crvs
