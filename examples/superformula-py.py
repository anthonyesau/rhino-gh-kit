"""@component
{
  "name":        "Superformula PY",
  "nickname":    "Superform",
  "description": "Nested superformula blooms - the Gielis supershape with Lobes petals, repeated in Layers shrinking and slowly rotating copies so the outlines interleave like a pressed flower. Points are the outermost outline samples; Curves are all the nested outlines. Unwired inputs fall back to a 7-lobe bloom of width 10.",
  "icon":        "icons/superformula-py.svg",
  "category":    "Vector",
  "subcategory": "Pattern",

  "inputs": [
    { "name": "Count", "nickname": "N", "type": "int", "access": "item",
      "description": "Samples around each outline (default 720)." },
    { "name": "Scale", "nickname": "S", "type": "double", "access": "item",
      "description": "Overall bloom width in model units (default 10)." },
    { "name": "BasePlane", "nickname": "P", "type": "Plane", "access": "item",
      "description": "Plane the bloom lies on (default world XY)." },
    { "name": "Lobes", "nickname": "m", "type": "int", "access": "item",
      "description": "Petal count of the supershape (default 7)." },
    { "name": "Layers", "nickname": "L", "type": "int", "access": "item",
      "description": "How many nested copies to draw (default 6)." }
  ],

  "outputs": [
    { "name": "Points", "nickname": "P", "type": "Point3d", "access": "list",
      "description": "Sample points of the outermost outline." },
    { "name": "Curves", "nickname": "C", "type": "Curve", "access": "list",
      "description": "The nested outlines, outermost first." }
  ]
}
"""
# Shares Count / Scale / BasePlane -> Points / Curves with the other pattern
# scripts, so re-forging a component between them keeps those wires.
import math
import Rhino.Geometry as rg

n = int(Count) if Count else 720
size = float(Scale) if Scale else 10.0
plane = BasePlane if BasePlane else rg.Plane.WorldXY
m = int(Lobes) if Lobes else 7
layers = int(Layers) if Layers else 6

n1, n2, n3 = 0.35, 1.7, 1.7

def radius(phi):
    a = abs(math.cos(m * phi / 4.0)) ** n2
    b = abs(math.sin(m * phi / 4.0)) ** n3
    return (a + b) ** (-1.0 / n1) if (a + b) > 1e-12 else 0.0

base = [radius(2.0 * math.pi * k / n) for k in range(n)]
peak = max(base) or 1.0
f = (size * 0.5) / peak

pts = []
crvs = []
for L in range(layers):
    sc = 1.0 - 0.75 * L / layers
    rot = L * math.pi / m  # half a petal per layer, so lobes interleave
    ring = []
    for k in range(n):
        phi = 2.0 * math.pi * k / n
        r = base[k] * f * sc
        ring.append(plane.PointAt(r * math.cos(phi + rot), r * math.sin(phi + rot), 0.0))
    ring.append(ring[0])
    crvs.append(rg.PolylineCurve(ring))
    if L == 0:
        pts = ring[:-1]

Points = pts
Curves = crvs
