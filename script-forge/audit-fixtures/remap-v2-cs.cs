/* @component
{
  "name":        "Audit Remap",
  "nickname":    "ARemap2",
  "description": "Remaps a number. V2 renames Value to Amount to test positional wire recycling.",
  "icon":        "icons/curve-frames.svg",
  "category":    "Audit",
  "subcategory": "Experimental",

  "inputs": [
    { "name": "Amount", "type": "double", "access": "item",
      "description": "The number to remap (was Value)." },
    { "name": "Source", "type": "Interval", "access": "item",
      "description": "The interval the value lives in." },
    { "name": "Target", "type": "Interval", "access": "item",
      "description": "The interval to map into." }
  ],

  "outputs": [
    { "name": "Mapped", "type": "double", "access": "item",
      "description": "The remapped number." }
  ]
}
*/
using System;
using Grasshopper.Kernel;
public class Script_Instance : GH_ScriptInstance
{
  private void RunScript(double Amount, Rhino.Geometry.Interval Source, Rhino.Geometry.Interval Target, out object Mapped)
  {
    double t = Source.NormalizedParameterAt(Amount);
    Mapped = Target.ParameterAt(t);
  }
}
