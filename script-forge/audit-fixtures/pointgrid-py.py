"""@component
{
  "name":        "Audit Point Grid",
  "nickname":    "APGrid",
  "description": "Builds a grid of points. Audit fixture for Python param sync and list output.",

  "inputs": [
    { "name": "Nx", "type": "int", "access": "item",
      "description": "Number of columns." },
    { "name": "Ny", "type": "int", "access": "item",
      "description": "Number of rows." }
  ],

  "outputs": [
    { "name": "Points", "type": "Point3d", "access": "list",
      "description": "The grid points." }
  ]
}
"""
import Rhino.Geometry as rg
pts = []
for i in range(Nx or 3):
    for j in range(Ny or 3):
        pts.append(rg.Point3d(i, j, 0))
Points = pts
