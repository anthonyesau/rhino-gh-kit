"""@component
{
  "name":        "Pinned Spiral PY",
  "nickname":    "Pinned",
  "description": "Shows what a header instanceGuid does. This source names the component it owns, so re-forging it with Target unwired updates that same component instead of dropping another copy beside it.",
  "icon":        "icons/spirograph-py.svg",
  "instanceGuid": "33f0a509-752c-4009-91a9-74500215b592",

  "inputs": [
    { "name": "Turns", "type": "double", "access": "item", "default": 5.0,
      "description": "How many revolutions the spiral makes." },
    { "name": "Radius", "type": "double", "access": "item", "default": 6.0,
      "description": "Radius at the outermost turn, in model units." }
  ],

  "outputs": [
    { "name": "Spiral", "type": "Curve", "access": "item",
      "description": "The spiral as an interpolated curve." }
  ]
}
"""
import math
import Rhino.Geometry as rg

turns = float(Turns) if Turns else 5.0
r = float(Radius) if Radius else 6.0
n = int(turns * 64)

pts = []
for k in range(n + 1):
    t = k / float(n)
    a = 2.0 * math.pi * turns * t
    pts.append(rg.Point3d(r * t * math.cos(a), r * t * math.sin(a), 0.0))

Spiral = rg.Curve.CreateInterpolatedCurve(pts, 3)
