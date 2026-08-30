"""@component
{
  "name":        "Halvorsen Attractor PY",
  "nickname":    "Halvorsen",
  "description": "Traces the Halvorsen strange attractor - a chaotic flow with three-fold symmetry - by RK4 integration, then fits the trajectory into a box of size Scale on the base plane. Points are the trajectory samples; Curves is a single polyline threading them. Unwired inputs fall back to 4000 samples in a size-10 box.",
  "icon":        "icons/halvorsen-attractor-py.svg",
  "category":    "Vector",
  "subcategory": "Pattern",
  "language":    "python",

  "inputs": [
    { "name": "Count", "nickname": "N", "type": "int", "access": "item",
      "description": "Number of trajectory samples (default 4000)." },
    { "name": "Scale", "nickname": "S", "type": "double", "access": "item",
      "description": "The trajectory is scaled to fit a box this wide (default 10)." },
    { "name": "BasePlane", "nickname": "P", "type": "Plane", "access": "item",
      "description": "Plane the attractor is mapped onto (default world XY)." },
    { "name": "StepSize", "nickname": "dt", "type": "double", "access": "item",
      "description": "Integration time step, smaller is smoother (default 0.01)." }
  ],

  "outputs": [
    { "name": "Points", "nickname": "P", "type": "Point3d", "access": "list",
      "description": "The trajectory sample points, in time order." },
    { "name": "Curves", "nickname": "C", "type": "Curve", "access": "list",
      "description": "A single polyline through the trajectory." }
  ]
}
"""
# Wire-preservation demo trio (with Phyllo Dome PY and Hex Jali PY): all three
# share Count / Scale / BasePlane -> Points / Curves, so re-forging one
# component with a sibling script keeps those wires. Only the unique fourth
# input (here: StepSize) differs.
import Rhino.Geometry as rg

n = int(Count) if Count else 4000
size = float(Scale) if Scale else 10.0
plane = BasePlane if BasePlane else rg.Plane.WorldXY
dt = float(StepSize) if StepSize else 0.01

A = 1.89  # classic Halvorsen coefficient

def deriv(p):
    x, y, z = p
    return (
        -A * x - 4.0 * y - 4.0 * z - y * y,
        -A * y - 4.0 * z - 4.0 * x - z * z,
        -A * z - 4.0 * x - 4.0 * y - x * x,
    )

def rk4(p, h):
    k1 = deriv(p)
    k2 = deriv(tuple(p[i] + 0.5 * h * k1[i] for i in range(3)))
    k3 = deriv(tuple(p[i] + 0.5 * h * k2[i] for i in range(3)))
    k4 = deriv(tuple(p[i] + h * k3[i] for i in range(3)))
    return tuple(p[i] + h / 6.0 * (k1[i] + 2 * k2[i] + 2 * k3[i] + k4[i]) for i in range(3))

p = (0.1, 0.0, 0.0)
for _ in range(300):  # discard the transient run-in
    p = rk4(p, dt)

raw = []
for _ in range(n):
    p = rk4(p, dt)
    raw.append(p)

# Fit the trajectory into a Scale-wide box centered on the plane origin.
lo = [min(c[i] for c in raw) for i in range(3)]
hi = [max(c[i] for c in raw) for i in range(3)]
mid = [(lo[i] + hi[i]) * 0.5 for i in range(3)]
extent = max(hi[i] - lo[i] for i in range(3))
f = size / extent if extent > 0 else 1.0

pts = [plane.PointAt((c[0] - mid[0]) * f, (c[1] - mid[1]) * f, (c[2] - mid[2]) * f) for c in raw]

Points = pts
Curves = [rg.PolylineCurve(pts)]
