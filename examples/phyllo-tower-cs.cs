/* @component
{
  "name":        "Phyllo Tower CS",
  "nickname":    "Tower",
  "description": "Cylindrical phyllotaxis - Count seeds wind up a vase-profiled tower by the golden angle, each carrying an outward-facing circle sized to the local packing, so the result reads like a pinecone. Points are the seed centers; Curves are the scale circles. Unwired inputs fall back to a 300-seed tower of radius 5.",
  "icon":        "icons/phyllo-tower-cs.svg",
  "category":    "Vector",
  "subcategory": "Pattern",

  "inputs": [
    { "name": "Count", "nickname": "N", "type": "int", "access": "item",
      "description": "Number of seed points (default 300)." },
    { "name": "Scale", "nickname": "S", "type": "double", "access": "item",
      "description": "Base radius of the tower in model units (default 5)." },
    { "name": "BasePlane", "nickname": "P", "type": "Plane", "access": "item",
      "description": "Plane the tower stands on (default world XY)." },
    { "name": "Height", "nickname": "H", "type": "double", "access": "item",
      "description": "Tower height as a multiple of Scale (default 3)." }
  ],

  "outputs": [
    { "name": "Points", "nickname": "P", "type": "Point3d", "access": "list",
      "description": "Seed center points, winding bottom to top." },
    { "name": "Curves", "nickname": "C", "type": "Curve", "access": "list",
      "description": "One outward-facing circle per seed, sized to the local spacing." }
  ]
}
*/

// Wire-preservation demo trio (with Aizawa Attractor CS and Girih Stars CS):
// all three share Count / Scale / BasePlane -> Points / Curves, so re-forging
// one component with a sibling script keeps those wires. Only the unique
// fourth input (here: Height) differs.

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
      double Height,
      out List<Point3d> Points,
      out List<Curve> Curves)
  {
    int n = Count > 0 ? Count : 300;
    double radius = Scale > 0 ? Scale : 5.0;
    Plane plane = BasePlane.IsValid ? BasePlane : Plane.WorldXY;
    double height = (Height > 0 ? Height : 3.0) * radius;

    double golden = Math.PI * (3.0 - Math.Sqrt(5.0)); // ~137.5077 degrees
    double scaleR = 0.65 * radius * Math.Sqrt(height / radius / n); // seed circle from per-seed surface area

    var pts = new List<Point3d>();
    var crvs = new List<Curve>();
    for (int i = 0; i < n; i++)
    {
      double t = (i + 0.5) / n;
      double a = i * golden;
      double r = radius * (0.7 + 0.3 * Math.Sin(Math.PI * t)); // vase profile
      double ca = Math.Cos(a);
      double sa = Math.Sin(a);
      Point3d pt = plane.PointAt(r * ca, r * sa, height * t);
      pts.Add(pt);

      Vector3d outward = ca * plane.XAxis + sa * plane.YAxis;
      crvs.Add(new Circle(new Plane(pt, outward), scaleR).ToNurbsCurve());
    }

    Points = pts;
    Curves = crvs;
  }
}
