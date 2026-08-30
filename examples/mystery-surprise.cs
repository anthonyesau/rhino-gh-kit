/* @component
{
  "name":        "Mystery Surprise",
  "nickname":    "Mystery",
  "description": "What could it be?",
  "icon":        "base64:iVBORw0KGgoAAAANSUhEUgAAABgAAAAYCAYAAADgdz34AAAAAXNSR0IArs4c6QAAAERlWElmTU0AKgAAAAgAAYdpAAQAAAABAAAAGgAAAAAAA6ABAAMAAAABAAEAAKACAAQAAAABAAAAGKADAAQAAAABAAAAGAAAAADiNXWtAAACnklEQVRIDe2US2hTURCGb5JicmNRIzELDSWQLIQ8sEUXilCEgg8QWqhSBLsSRJcupC5cawU3oiKC0IXoSqQupKhVqQsXIpa8NCglPrBGIWCKKHnc+J2YCTf3XrPtpgcmc2b+mflnzjk3mra2VvsEXP9rIBaLbfB6vZvcbvd4s9mMulyubcSm0ANoj+TV6/WhfD7/Rmyr7rM6xNZ1/QnFdikbLW6b9ng813DusQFthyMB3Xvp+oYQSDK+Jr4WW61WS75lgTUEd9K21pLJ5GNqjBD8lXq32D80DOMnR7WAL9gu8gv9A/wZ+gXHNAfXchvrUk4EiyQcNCfE4/EBCCYgm1ZTZDKZdfgG8e2j2iH8w7ir6PuNRuNmLpd7Kiw2AgEosIMCk+0CmynwkQI7FZ5Op215TL4b6Awx4+YYtzJkRSIRXyKROEfwN4rP469xPBMUDLG/K3HgU7IXzVQvkSPYl8WndOeS6bifF7FCp5+RyWw2+8gcCNEd8FYyXV4Au2jC+8g/Bq78W5HtgnVGTaVS1G0u0UVUQKtWMeIrlUr9wWDwMJMehXCM3NfIdRqbIcaQuM4EykHgFQGcNAUMYlrHGgqFysTM47vHdCe4WGXbVmcCzj5KNx9IUM/xPJMshMNhPRAIjGGPIgeQ7xCoCRe5F3XhPb8BcK1DoAw+sC1+v/8sJKcw/RRzsX9HhzN8WLd9Pt8XFaeW00v6h3T/dhEoiBfynLrDFL5E4WWmGsHeC7TRnFqpVPRisfjH7HPa2whMFzkHwQOSXpXL5TwXqr7e1oL8E6TqT0/s2Wq1erxQKKyIT7SNAMADyUmK7Gc/RKGwBPfSfP2j/KvOWmOcCKwxGoSncap/zd/IErIeiSAajVxF3vM8e75AFbu2VucE/gK1AhRp6GgysgAAAABJRU5ErkJggg==",

  "inputs": [
    { "name": "Count", "nickname": "N", "type": "int", "access": "item",
      "description": "Number of trajectory samples (default 4000)." },
    { "name": "Scale", "nickname": "S", "type": "double", "access": "item",
      "description": "The trajectory is scaled to fit a box this wide (default 10)." },
    { "name": "BasePlane", "nickname": "P", "type": "Plane", "access": "item",
      "description": "Plane the attractor is mapped onto (default world XY)." },
    { "name": "StepSize", "nickname": "dt", "type": "double", "access": "item",
      "description": "Integration time step, smaller is smoother (default 0.01)." }
  ],

  "outputs": [
    { "name": "Points", "nickname": "P", "type": "Point3d", "access": "list",
      "description": "The trajectory sample points, in time order." },
    { "name": "Curves", "nickname": "C", "type": "Curve", "access": "list",
      "description": "A single polyline through the trajectory." }
  ]
}
*/

// Wire-preservation demo trio (with Phyllo Tower CS and Girih Stars CS):
// all three share Count / Scale / BasePlane -> Points / Curves, so re-forging
// one component with a sibling script keeps those wires. Only the unique
// fourth input (here: StepSize) differs.

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
      double StepSize,
      out List<Point3d> Points,
      out List<Curve> Curves)
  {
    int n = Count > 0 ? Count : 4000;
    double size = Scale > 0 ? Scale : 10.0;
    Plane plane = BasePlane.IsValid ? BasePlane : Plane.WorldXY;
    double dt = StepSize > 0 ? StepSize : 0.01;

    Vector3d p = new Vector3d(0.1, 0.0, 0.0);
    for (int i = 0; i < 300; i++) // discard the transient run-in
      p = Rk4(p, dt);

    var raw = new List<Vector3d>(n);
    for (int i = 0; i < n; i++)
    {
      p = Rk4(p, dt);
      raw.Add(p);
    }

    // Fit the trajectory into a size-wide box centered on the plane origin.
    Vector3d lo = raw[0], hi = raw[0];
    foreach (Vector3d v in raw)
    {
      lo = new Vector3d(Math.Min(lo.X, v.X), Math.Min(lo.Y, v.Y), Math.Min(lo.Z, v.Z));
      hi = new Vector3d(Math.Max(hi.X, v.X), Math.Max(hi.Y, v.Y), Math.Max(hi.Z, v.Z));
    }
    Vector3d mid = 0.5 * (lo + hi);
    double extent = Math.Max(hi.X - lo.X, Math.Max(hi.Y - lo.Y, hi.Z - lo.Z));
    double f = extent > 0 ? size / extent : 1.0;

    var pts = new List<Point3d>(n);
    foreach (Vector3d v in raw)
      pts.Add(plane.PointAt((v.X - mid.X) * f, (v.Y - mid.Y) * f, (v.Z - mid.Z) * f));

    Points = pts;
    Curves = new List<Curve> { new PolylineCurve(pts) };
  }

  // Aizawa system, classic coefficients.
  private static Vector3d Deriv(Vector3d p)
  {
    const double a = 0.95, b = 0.7, c = 0.6, d = 3.5, e = 0.25, f = 0.1;
    double x = p.X, y = p.Y, z = p.Z;
    return new Vector3d(
        (z - b) * x - d * y,
        d * x + (z - b) * y,
        c + a * z - z * z * z / 3.0 - (x * x + y * y) * (1.0 + e * z) + f * z * x * x * x);
  }

  private static Vector3d Rk4(Vector3d p, double h)
  {
    Vector3d k1 = Deriv(p);
    Vector3d k2 = Deriv(p + 0.5 * h * k1);
    Vector3d k3 = Deriv(p + 0.5 * h * k2);
    Vector3d k4 = Deriv(p + h * k3);
    return p + (h / 6.0) * (k1 + 2.0 * k2 + 2.0 * k3 + k4);
  }
}

