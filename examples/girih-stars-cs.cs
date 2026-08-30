/* @component
{
  "name":        "Girih Stars CS",
  "nickname":    "Girih",
  "description": "A girih-style star screen - a Count by Count grid of cells, each framed by a square and holding an eight-pointed star in the manner of the Rub el Hizb motif. Points are the cell centers; Curves are the frames and stars. Unwired inputs fall back to a 4 by 4 panel of width 10.",
  "icon":        "icons/girih-stars-cs.svg",
  "category":    "Vector",
  "subcategory": "Pattern",

  "inputs": [
    { "name": "Count", "nickname": "N", "type": "int", "access": "item",
      "description": "Cells per side of the square panel (default 4)." },
    { "name": "Scale", "nickname": "S", "type": "double", "access": "item",
      "description": "Overall panel width in model units (default 10)." },
    { "name": "BasePlane", "nickname": "P", "type": "Plane", "access": "item",
      "description": "Plane the panel lies on (default world XY)." },
    { "name": "StarDepth", "nickname": "D", "type": "double", "access": "item",
      "description": "Inner radius of each star as a fraction of its points, 0 to 1 (default 0.4)." }
  ],

  "outputs": [
    { "name": "Points", "nickname": "P", "type": "Point3d", "access": "list",
      "description": "The center point of every cell." },
    { "name": "Curves", "nickname": "C", "type": "Curve", "access": "list",
      "description": "Cell frames and star polygons, cell by cell." }
  ]
}
*/

// Wire-preservation demo trio (with Phyllo Tower CS and Aizawa Attractor CS):
// all three share Count / Scale / BasePlane -> Points / Curves, so re-forging
// one component with a sibling script keeps those wires. Only the unique
// fourth input (here: StarDepth) differs.

using System;
using System.Collections.Generic;

using Rhino.Geometry;
using Grasshopper.Kernel;

public class Script_Instance : GH_ScriptInstance
{
  private void RunScript(
      int Count,
      double Scale,
      Plane BasePlane,
      double StarDepth,
      out List<Point3d> Points,
      out List<Curve> Curves)
  {
    int n = Count > 0 ? Count : 4;
    double size = Scale > 0 ? Scale : 10.0;
    Plane plane = BasePlane.IsValid ? BasePlane : Plane.WorldXY;
    double depth = StarDepth > 0 ? StarDepth : 0.4;

    double cell = size / n;
    double r0 = 0.48 * cell; // outer star radius, just inside the cell frame

    var pts = new List<Point3d>();
    var crvs = new List<Curve>();
    for (int j = 0; j < n; j++)
    {
      for (int i = 0; i < n; i++)
      {
        double cx = -size * 0.5 + (i + 0.5) * cell;
        double cy = -size * 0.5 + (j + 0.5) * cell;
        pts.Add(plane.PointAt(cx, cy, 0.0));

        // Cell frame.
        double h = cell * 0.5;
        var frame = new Polyline
        {
          plane.PointAt(cx - h, cy - h, 0.0),
          plane.PointAt(cx + h, cy - h, 0.0),
          plane.PointAt(cx + h, cy + h, 0.0),
          plane.PointAt(cx - h, cy + h, 0.0),
          plane.PointAt(cx - h, cy - h, 0.0),
        };
        crvs.Add(new PolylineCurve(frame));

        // Eight-pointed star: 16 vertices alternating outer and inner radius.
        var star = new Polyline();
        for (int k = 0; k < 16; k++)
        {
          double ang = k * Math.PI / 8.0;
          double rad = (k % 2 == 0) ? r0 : r0 * depth;
          star.Add(plane.PointAt(cx + rad * Math.Cos(ang), cy + rad * Math.Sin(ang), 0.0));
        }
        star.Add(star[0]);
        crvs.Add(new PolylineCurve(star));
      }
    }

    Points = pts;
    Curves = crvs;
  }
}
