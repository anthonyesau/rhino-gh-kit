/* @component
{
  "name":        "Audit Remap",
  "nickname":    "ARemap",
  "description": "Remaps a number from one interval into another. Audit fixture.",
  "icon":        "icons/curve-frames.svg",
  "category":    "Audit",
  "subcategory": "Experimental",

  "inputs": [
    { "name": "Value", "type": "double", "access": "item",
      "description": "The number to remap." },
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
  private void RunScript(double Value, Rhino.Geometry.Interval Source, Rhino.Geometry.Interval Target, out object Mapped)
  {
    double t = Source.NormalizedParameterAt(Value);
    Mapped = Target.ParameterAt(t);
  }
}
