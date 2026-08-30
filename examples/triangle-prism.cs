/* @component
{
  "name":        "Triangle Prism",
  "nickname":    "Tri Prism",
  "description": "Builds a triangle from three points and extrudes it by a distance to make a triangular prism.",
  "icon":        "icons/triangle-prism.svg",
  "category":    "Surface",
  "subcategory": "Primitive",

  "inputs": [
    { "name": "PointA", "nickname": "A", "type": "Point3d", "access": "item",
      "description": "First corner of the triangle." },
    { "name": "PointB", "nickname": "B", "type": "Point3d", "access": "item",
      "description": "Second corner of the triangle." },
    { "name": "PointC", "nickname": "C", "type": "Point3d", "access": "item",
      "description": "Third corner of the triangle." },
    { "name": "Distance", "nickname": "D", "type": "double", "access": "item",
      "description": "Extrusion distance along the triangle's normal (negative flips direction)." }
  ],

  "outputs": [
    { "name": "Triangle", "nickname": "T", "type": "Curve", "access": "item",
      "description": "The closed triangle curve." },
    { "name": "Prism", "nickname": "P", "type": "Brep", "access": "item",
      "description": "The extruded triangular prism, capped at both ends." }
  ]
}
*/

using System;
using System.Collections.Generic;

using Rhino.Geometry;
using Grasshopper.Kernel;

public class Script_Instance : GH_ScriptInstance
{
  private void RunScript(
      Point3d PointA,
      Point3d PointB,
      Point3d PointC,
      double Distance,
      out Curve Triangle,
      out Brep Prism)
  {
    Polyline outline = new Polyline(new Point3d[] { PointA, PointB, PointC, PointA });
    Curve triangleCurve = outline.ToNurbsCurve();

    Extrusion extrusion = Extrusion.Create(triangleCurve, Distance, true);
    Brep prismBrep = extrusion != null ? extrusion.ToBrep() : null;

    Triangle = triangleCurve;
    Prism = prismBrep;
  }
}
