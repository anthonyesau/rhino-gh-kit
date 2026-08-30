"""@component
{
  "name":        "Ring Array (Shared Labels)",
  "nickname":    "RingArr",
  "description": "Ring Array variant built to demonstrate that a live Python 3 Script component can draw the same NickName on an input and an output -- three of its four pairs below are named identically across sides, and each output pair computes something genuinely different from its same-named input rather than passing it through.",
  "language":    "python",

  "inputs": [
    { "name": "Geometry", "nickname": "G", "type": "GeometryBase", "access": "item",
      "description": "Shape to copy around the ring. Unwired: points/count still compute, but no copies are placed." },
    { "name": "Radius", "nickname": "R", "type": "double", "access": "item", "default": 5.0,
      "description": "Flat ring radius." },
    { "name": "Count", "nickname": "N", "type": "int", "access": "item", "default": 8,
      "description": "Number of copies requested around the ring." },
    { "name": "Height", "nickname": "H", "type": "double", "access": "item", "default": 0.0,
      "description": "Vertical rise per copy -- 0 is a flat ring, nonzero turns it into a spiral." },
    { "name": "Align", "nickname": "A", "type": "bool", "access": "item", "default": true,
      "description": "True: rotate each copy to face outward along the ring. False: keep the source orientation, just translated." }
  ],

  "outputs": [
    { "name": "Geometry", "nickname": "G", "type": "GeometryBase", "access": "list",
      "description": "SAME LABEL as the Geometry input, on purpose -- an entirely different shape (a list of placed copies vs. the single source item). Proves the shared label is cosmetic, not a data alias: this variable holds the single source item until the last line of the script reassigns it to the list of copies." },
    { "name": "Points", "nickname": "P", "type": "Point3d", "access": "list",
      "description": "The ring/spiral placement points -- one per requested copy, regardless of whether Geometry was wired." },
    { "name": "Radius", "nickname": "R", "type": "double", "access": "item",
      "description": "SAME LABEL as the Radius input, but not the same number once Height != 0: this is the actual 3D distance from the origin to the furthest placed point, sqrt(Radius^2 + zmax^2)." },
    { "name": "Count", "nickname": "N", "type": "int", "access": "item",
      "description": "SAME LABEL as the Count input, but reports how many copies were actually placed -- 0 whenever Geometry is unwired, even though Count was requested." }
  ]
}
"""
import math
import Rhino.Geometry as rg

n = max(Count, 0) if Count else 0
r = Radius if Radius else 0.0
h = Height if Height else 0.0
align = bool(Align)

placements = []
copies = []
placed = 0
z_max = 0.0

for i in range(n):
    t = 2 * math.pi * i / max(n, 1)
    z = h * i
    pt = rg.Point3d(r * math.cos(t), r * math.sin(t), z)
    placements.append(pt)
    z_max = max(z_max, abs(z))

    if Geometry is None:
        continue
    copy = Geometry.Duplicate()
    if align:
        frame = rg.Plane(pt, rg.Vector3d.ZAxis)
        frame.Rotate(t, rg.Vector3d.ZAxis, pt)
        copy.Transform(rg.Transform.PlaneToPlane(rg.Plane.WorldXY, frame))
    else:
        copy.Translate(rg.Vector3d(pt.X, pt.Y, pt.Z))
    copies.append(copy)
    placed += 1

actual_radius = math.sqrt(r * r + z_max * z_max)

# Reassign the three shared names LAST, after every read of the input value
# above -- this is the whole mechanism: same NickName, same Python global,
# input value consumed before the output value overwrites it.
Points = placements
Geometry = copies
Radius = actual_radius
Count = placed
