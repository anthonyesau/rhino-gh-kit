"""@component
{
  "name":        "Watch Me PY",
  "nickname":    "WatchMe",
  "description": "A file-watcher demo. Draws a ring of points on a circle. Edit this file in any editor, save it, and Script Forge re-forges the component on the canvas by itself - no second button press.",
  "icon":        "icons/point-grid-py.svg",

  "inputs": [
    { "name": "Count", "type": "int", "access": "item", "default": 24,
      "description": "How many points to place around the ring." },
    { "name": "Radius", "type": "double", "access": "item", "default": 5.0,
      "description": "Ring radius in model units." }
  ],

  "outputs": [
    { "name": "Points", "type": "Point3d", "access": "list",
      "description": "The ring points, counter-clockwise from the X axis." },
    { "name": "Ring", "type": "Curve", "access": "item",
      "description": "A closed polyline through the points." }
  ]
}
"""
# --- Try this: change the 2.0 below to 3.0 and save the file. ---
# With Run held true, the component on the canvas rebuilds a moment later.
import math
import Rhino.Geometry as rg

n = int(Count) if Count else 24
r = float(Radius) if Radius else 5.0
lobes = 2.0        # <-- edit me, then save

pts = []
for k in range(n):
    a = 2.0 * math.pi * k / n
    rr = r * (1.0 + 0.25 * math.cos(lobes * a))
    pts.append(rg.Point3d(rr * math.cos(a), rr * math.sin(a), 0.0))

Points = pts
Ring = rg.PolylineCurve(pts + [pts[0]])
