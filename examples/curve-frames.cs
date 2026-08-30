/* @component
{
  "name":        "Curve Frames",
  "nickname":    "Frames",
  "description": "Divides a curve into equal-parameter segments and returns a frame at each division point. This description wraps across three source lines to demonstrate the header continuation syntax.",
  "icon":        "icons/curve-frames.svg",
  "category":    "Curve",
  "subcategory": "Division",

  "inputs": [
    { "name": "Path", "nickname": "C", "type": "Curve", "access": "item",
      "description": "The curve to divide. Left unwired, the outputs are empty." },
    { "name": "Count", "nickname": "N", "type": "int", "access": "item",
      "description": "Number of segments to divide the curve into (values below 1 are treated as 10)." },
    { "name": "Perp", "nickname": "P", "type": "bool", "access": "item",
      "description": "When true, frames are perpendicular to the curve; when false, aligned to curvature." }
  ],

  "outputs": [
    { "name": "Planes", "nickname": "P", "type": "Plane", "access": "list",
      "description": "One frame per division point." },
    { "name": "Parameters", "nickname": "t", "type": "double", "access": "list",
      "description": "The curve parameter at each division point." }
  ]
}
*/

using System;
using System.Collections.Generic;

using Grasshopper.Kernel;
using Rhino.Geometry;

public class Script_Instance : GH_ScriptInstance
{
  private void RunScript(Curve Path, int Count, bool Perp, out object Planes, out object Parameters)
  {
    var planes = new List<Plane>();
    var ts = new List<double>();

    if (Path != null && Path.IsValid)
    {
      int n = Count < 1 ? 10 : Count;
      var prms = Path.DivideByCount(n, true);
      if (prms != null)
      {
        foreach (var t in prms)
        {
          Plane pl;
          bool ok = Perp ? Path.PerpendicularFrameAt(t, out pl) : Path.FrameAt(t, out pl);
          if (!ok) continue;
          planes.Add(pl);
          ts.Add(t);
        }
      }
    }

    Planes = planes;
    Parameters = ts;
  }
}
