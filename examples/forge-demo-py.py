"""@component
{
  "name":        "Forge Demo PY",
  "nickname":    "DemoPY",
  "description": "Tiny Python 3 demo built by Script Forge - moves a point along a scaled vector and reports the travel distance. Unwired inputs fall back to the world origin, a unit Z vector, and scale 1.",
  "icon":        "icons/forge-demo-py.svg",
  "category":    "Maths",
  "subcategory": "Util",

  "inputs": [
    { "name": "Point", "nickname": "P", "type": "Point3d", "access": "item",
      "description": "The point to move (default world origin when unwired)." },
    { "name": "Motion", "nickname": "V", "type": "Vector3d", "access": "item",
      "description": "The vector to move the point along (default unit Z)." },
    { "name": "Scale", "nickname": "S", "type": "double", "access": "item",
      "description": "Multiplier applied to the vector before moving (default 1)." }
  ],

  "outputs": [
    { "name": "Moved", "nickname": "P", "type": "Point3d", "access": "item",
      "description": "The moved point." },
    { "name": "Travel", "nickname": "D", "type": "double", "access": "item",
      "description": "The distance moved." }
  ]
}
"""
import Rhino.Geometry as rg

p = rg.Point3d(Point) if Point is not None else rg.Point3d(0.0, 0.0, 0.0)
v = rg.Vector3d(Motion) if Motion is not None else rg.Vector3d(0.0, 0.0, 1.0)
v *= float(Scale) if Scale is not None else 1.0

Moved = p + v
Travel = v.Length
