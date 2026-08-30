"""@component
{
  "name":        "Point Grid PY",
  "nickname":    "PGrid",
  "description": "Builds a rectangular grid of points - a Python 3 example using a docstring header with int, double, and Point3d type hints. Unwired inputs fall back to a 3x3 grid with unit spacing at the origin.",
  "icon":        "icons/point-grid-py.svg",
  "category":    "Vector",
  "subcategory": "Grid",

  "inputs": [
    { "name": "CountX", "nickname": "Nx", "type": "int", "access": "item",
      "description": "Points along X (default 3 when unwired)." },
    { "name": "CountY", "nickname": "Ny", "type": "int", "access": "item",
      "description": "Points along Y (default 3 when unwired)." },
    { "name": "Spacing", "nickname": "S", "type": "double", "access": "item",
      "description": "Distance between neighboring points (default 1.0)." },
    { "name": "Origin", "nickname": "O", "type": "Point3d", "access": "item",
      "description": "Lower-left corner of the grid (default world origin)." }
  ],

  "outputs": [
    { "name": "Points", "nickname": "P", "type": "Point3d", "access": "list",
      "description": "The grid points, row by row." },
    { "name": "Total", "nickname": "N", "type": "int", "access": "item",
      "description": "How many points were generated." }
  ]
}
"""
import Rhino.Geometry as rg

nx = CountX if CountX else 3
ny = CountY if CountY else 3
s = Spacing if Spacing else 1.0
o = rg.Point3d(Origin) if Origin else rg.Point3d(0, 0, 0)

pts = []
for j in range(ny):
    for i in range(nx):
        pts.append(rg.Point3d(o.X + i * s, o.Y + j * s, o.Z))

Points = pts
Total = len(pts)

