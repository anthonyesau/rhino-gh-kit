/* @component
{
  "name":        "Labels Demo CS",
  "nickname":    "Labels",
  "description": "Shows the two label slots on a script param. 'name' is the tooltip title; 'variableName' is the pin label AND the identifier the code receives. Hover each pin to see the pair.",
  "icon":        "icons/branch-sums.svg",

  "inputs": [
    { "name": "Max. Radius", "variableName": "MaxRadius", "type": "double", "access": "item", "default": 8.0,
      "description": "The label wanted here is 'Max. Radius', which is not a legal identifier - so variableName gives the code a usable 'MaxRadius'." },
    { "name": "Count", "variableName": "InCount", "type": "int", "access": "item", "default": 12,
      "description": "An input and an output both want the label 'Count'. They cannot share one variable, so this one is 'InCount' - the tooltip title still reads Count." },
    { "name": "Radius", "type": "double", "access": "item", "default": 3.0,
      "description": "No variableName here, so both slots collapse to 'Radius' - the pin reads Radius and the code takes a Radius argument. This is the usual case." }
  ],

  "outputs": [
    { "name": "Count", "variableName": "OutCount", "type": "int", "access": "item",
      "description": "The same label as the input, on the other side. Its pin reads OutCount and its tooltip is titled Count (OutCount)." },
    { "name": "Circles", "type": "Curve", "access": "list",
      "description": "Concentric circles between Radius and Max. Radius, innermost first." }
  ]
}
*/

using System.Collections.Generic;
using Grasshopper.Kernel;
using Rhino.Geometry;

public class Script_Instance : GH_ScriptInstance
{
  private void RunScript(double MaxRadius, int InCount, double Radius,
                         out object OutCount, out object Circles)
  {
    var crvs = new List<Curve>();
    int n = InCount < 1 ? 1 : InCount;
    for (int i = 0; i < n; i++)
    {
      double t = n == 1 ? 0.0 : i / (double)(n - 1);
      double r = Radius + t * (MaxRadius - Radius);
      crvs.Add(new Circle(Plane.WorldXY, r).ToNurbsCurve());
    }
    Circles = crvs;
    OutCount = crvs.Count;
  }
}
