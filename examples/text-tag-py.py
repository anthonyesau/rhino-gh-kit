"""@component
{
  "name":        "Text Tag PY",
  "nickname":    "Tag",
  "description": "Creates a TextDot label at a point - demonstrates the explicit language field and the string and TextDot type hints.",
  "icon":        "icons/text-tag-py.svg",
  "category":    "Display",
  "subcategory": "Dimensions",
  "language":    "python",

  "inputs": [
    { "name": "Text", "nickname": "T", "type": "string", "access": "item",
      "description": "The label text (default 'tag' when unwired)." },
    { "name": "Location", "nickname": "P", "type": "Point3d", "access": "item",
      "description": "Where to place the dot (default world origin)." },
    { "name": "Size", "nickname": "S", "type": "int", "access": "item",
      "description": "Font height in pixels (default 14 when unwired or below 1)." }
  ],

  "outputs": [
    { "name": "Dot", "nickname": "D", "type": "TextDot", "access": "item",
      "description": "The text dot, ready to preview or bake." }
  ]
}
"""
import Rhino.Geometry as rg

msg = Text if Text else "tag"
pt = rg.Point3d(Location) if Location else rg.Point3d(0, 0, 0)

dot = rg.TextDot(msg, pt)
dot.FontHeight = Size if Size and Size > 0 else 14

Dot = dot
