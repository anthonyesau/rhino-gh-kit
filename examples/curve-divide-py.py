# @component
# {
#   "name":        "Curve Divide PY",
#   "nickname":    "CDiv",
#   "description": "Divides a curve into equal segments and returns the division points and parameters. This example uses the hash-comment header style and wraps its description across three lines to show continuation.",
#   "icon":        "icons/curve-divide-py.svg",
#   "category":    "Curve",
#   "subcategory": "Division",
#
#   "inputs": [
#     { "name": "Path", "nickname": "C", "type": "Curve", "access": "item",
#       "description": "The curve to divide. Left unwired, the outputs are empty." },
#     { "name": "Count", "nickname": "N", "type": "int", "access": "item",
#       "description": "Number of segments (values below 1 are treated as 10) - param descriptions can wrap too." }
#   ],
#
#   "outputs": [
#     { "name": "Points", "nickname": "P", "type": "Point3d", "access": "list",
#       "description": "The division points along the curve." },
#     { "name": "Parameters", "nickname": "t", "type": "double", "access": "list",
#       "description": "The curve parameter at each division point." }
#   ]
# }

import Rhino.Geometry as rg

Points = []
Parameters = []

if Path:
    n = Count if Count and Count > 0 else 10
    ts = Path.DivideByCount(n, True)
    if ts:
        Parameters = list(ts)
        Points = [Path.PointAt(t) for t in ts]

