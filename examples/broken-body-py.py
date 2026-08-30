"""@component
{
  "name":        "Broken Body PY",
  "nickname":    "Broken",
  "description": "The header is valid, so params and metadata are stamped - but the body has a syntax error, so the component itself goes red. Note that Success still reads TRUE: it reports the forge's own failures, not the target script's runtime errors.",
  "icon":        "icons/triangle-prism.svg",

  "inputs": [
    { "name": "Radius", "type": "double", "access": "item", "default": 4.0,
      "description": "Collected fine - the param list is built from the header, which parsed." }
  ],

  "outputs": [
    { "name": "Circle", "type": "Curve", "access": "item",
      "description": "Never produced; the body raises before it is assigned." }
  ]
}
"""
import Rhino.Geometry as rg

# The next line is deliberately wrong - Plane.WorldXY is misspelled.
Circle = rg.Circle(rg.Plane.WorldXZY, float(Radius)).ToNurbsCurve()
