"""@component
{
  "name":        "Good Source PY",
  "nickname":    "Good",
  "description": "The branch that works. Forged normally: Success reads true and its Log records header parse, param sync, compile and stamping.",
  "icon":        "icons/curve-frames.svg",

  "inputs": [
    { "name": "Radius", "type": "double", "access": "item", "default": 4.0,
      "description": "Radius of the circle, in model units." }
  ],

  "outputs": [
    { "name": "Circle", "type": "Curve", "access": "item",
      "description": "A circle on the world XY plane." }
  ]
}
"""
import Rhino.Geometry as rg

Circle = rg.Circle(rg.Plane.WorldXY, float(Radius) if Radius else 4.0).ToNurbsCurve()
