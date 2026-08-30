/* @component
{
  "name":        "Mystery Surprise",
  "nickname":    "Mystery",
  "description": "What could it be?",
  "icon":        "base64:iVBORw0KGgoAAAANSUhEUgAAABgAAAAYCAYAAADgdz34AAAAAXNSR0IArs4c6QAAAERlWElmTU0AKgAAAAgAAYdpAAQAAAABAAAAGgAAAAAAA6ABAAMAAAABAAEAAKACAAQAAAABAAAAGKADAAQAAAABAAAAGAAAAADiNXWtAAAC4klEQVRIDcWVO2iTURTH82geGBuEKFFbbNQaobFBEEFEh1ragqMWURxEF2fp6mbBx+BaF0UKLoI6qGAr+KDSrYhJ2tiYQa1RKKaDCSaQl79Tvwtf7v2CuOiFwz2P/3ncc8/9Ppfrf61EInEQOvmn/IIRbCecp6PB47nsdruPin1wcPA+dNUJKxgPWCeb6BwTUNFGbKM4PxRQq9Wahh4Lb1/xeHwzmDV0o5aP3bzOdxma34oxHEupVOqNiJlM5okTLhAIDIG7hK0MjUEPdJxbV4hMO+6wBaF9jUbj3NLS0lvRd1hdyWTyNidspNPpCzrGsUVUdazZbD6TtsB/050GBgYSBN1r6etgn4uPjhPZaBF97UHfB72iNdMC0pfX650k+U/0Z8XGKee46D7xzeVyBTveOIHf798P4Pvi4uJnO9DOE3yNql8qXTab/SQ+lq9Sr+9GAirZg2WZqdjKXexqQ1sC7ViGXbHbSJqzfO1qs0VUtp0ABegKyH5oqM0Dgcu8oeuQCyTZpuuNO6AKeQOlSqUywR7QHXSZk/ZzJ9cIXqaobt1uJFCAfD7/Q/hYLLYpFAp5uBN5UH+9jDugRWWqCatI4XD4FhXeVbK+kzjPgxxHvwG/km43TkCLvgI6rIDVanXC5/N5mftxkm9hdKeUTdt7aNG8pjO/RQT5AEg9IpfMtTWyveh3agF80Wg0JDqCxy3fNojxqZDHEgwGv9RqtZg1320OdoExvol8qF6vn+GUHzltr/7QvHYH4YvFYomqztMqL3t8dXV1QccoORKJvAf3muoPCPHNktFuW8Yli5XLesE2Ag2L3GG5OeEKb2KBJCOWjwF1TEAvn1LRbpxPGx6WgvZMQY8QJcZx8XHCOiYAOENF3QQ4YndikoaFREfAKTDXBcMuj3PGjlW8YwKmRn4gszieUEDZkU8JCQ/mHSM7b2FmLR8xtS3jHSgrFU7C71Cy7LTsol0WngRz0D1d/8/kX4MOKkZvInwWAAAAAElFTkSuQmCC",

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

