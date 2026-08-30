/* @component
{
  "name":        "Defaults Demo CS",
  "nickname":    "Defaults",
  "description": "Shows the header keys 'default' and 'optional'. Three inputs start out carrying a value even though nothing is wired to them; the fourth refuses to solve until something is.",
  "icon":        "icons/color-blend.svg",

  "inputs": [
    { "name": "Rows", "type": "int", "access": "item", "default": 4,
      "description": "Points per side of the grid. Starts at 4 with nothing wired - the header's 'default' becomes the param's persistent data." },
    { "name": "Spacing", "type": "double", "access": "item", "default": 1.5,
      "description": "Distance between points. Starts at 1.5 with nothing wired." },
    { "name": "Solid", "type": "bool", "access": "item", "default": true,
      "description": "Draw the boundary rectangle. Starts TRUE - which a plain unwired bool can never do, because Grasshopper hands the script false whether you wired false or wired nothing." },
    { "name": "Anchor", "type": "Point3d", "access": "item", "optional": false,
      "description": "Corner the grid is built from. Declared 'optional': false, so leaving it unwired turns the component orange and no code runs at all." }
  ],

  "outputs": [
    { "name": "Points", "type": "Point3d", "access": "list",
      "description": "The grid points, row by row." },
    { "name": "Border", "type": "Curve", "access": "item",
      "description": "The boundary rectangle, or null when Solid is false." }
  ]
}
*/

using System.Collections.Generic;
using Grasshopper.Kernel;
using Rhino.Geometry;

public class Script_Instance : GH_ScriptInstance
{
  private void RunScript(int Rows, double Spacing, bool Solid, Point3d Anchor,
                         out object Points, out object Border)
  {
    var pts = new List<Point3d>();
    for (int j = 0; j < Rows; j++)
      for (int i = 0; i < Rows; i++)
        pts.Add(new Point3d(Anchor.X + i * Spacing, Anchor.Y + j * Spacing, Anchor.Z));

    Points = pts;

    double w = (Rows - 1) * Spacing;
    Border = Solid
      ? new Rectangle3d(new Plane(Anchor, Vector3d.ZAxis),
                        new Interval(0, w), new Interval(0, w)).ToNurbsCurve()
      : null;
  }
}
