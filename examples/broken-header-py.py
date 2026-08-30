"""@component
{
  "name":        "Broken Header PY",
  "description": "This header has a trailing comma after the last input, which strict JSON refuses.",

  "inputs": [
    { "name": "Radius", "type": "double", "access": "item",
      "description": "Never reaches the canvas - the header never parses." },
  ]
}
"""
# gh-meta: ignore — deliberately malformed header; section 9 of the examples canvas
# forges this on purpose to show what a JSON failure looks like in Log.
import Rhino.Geometry as rg

Circle = rg.Circle(rg.Plane.WorldXY, 4.0).ToNurbsCurve()
