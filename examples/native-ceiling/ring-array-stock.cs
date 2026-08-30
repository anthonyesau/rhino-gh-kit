// Ring Array — copies one piece of geometry to a ring of points, rotating each
// copy outward. Wire a Polygon into Geometry for a rosette.
//
// The logic and nothing else: no param tooltips, no component name, no icon.
// Pasting it into the ScriptEditor builds the params and their type hints from the
// RunScript signature below, so it runs as it stands.

using System;
using System.Collections.Generic;
using Grasshopper.Kernel;
using Rhino.Geometry;

public class Script_Instance : GH_ScriptInstance
{
  private void RunScript(GeometryBase Geometry, double Radius, int Count,
                         out List<Point3d> Points, out List<GeometryBase> Arrayed)
  {
    // Outputs arrive as ref object whatever you declare, so build the results in
    // locals of the real type and assign once at the end.
    var pts = new List<Point3d>();
    var copies = new List<GeometryBase>();

    for (int i = 0; i < Count; i++)
    {
      double t = 2.0 * Math.PI * i / Math.Max(Count, 1);
      var pt = new Point3d(Radius * Math.Cos(t), Radius * Math.Sin(t), 0.0);
      pts.Add(pt);

      if (Geometry == null) continue;
      var frame = new Plane(pt, Vector3d.ZAxis);
      frame.Rotate(t, Vector3d.ZAxis, pt);
      var copy = Geometry.Duplicate();
      copy.Transform(Transform.PlaneToPlane(Plane.WorldXY, frame));
      copies.Add(copy);
    }

    Points = pts;
    Arrayed = copies;
  }
}
