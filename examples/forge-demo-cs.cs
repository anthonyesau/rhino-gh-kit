/* @component
{
  "name":        "Forge Demo CS",
  "nickname":    "DemoCS",
  "description": "Tiny C# demo built by Script Forge - remaps a number from a source interval into a target interval.",
  "icon":        "icons/forge-demo-cs.svg",
  "category":    "Maths",
  "subcategory": "Util",

  "inputs": [
    { "name": "Value", "nickname": "V", "type": "double", "access": "item",
      "description": "The number to remap from Source into Target." },
    { "name": "Source", "nickname": "S", "type": "Interval", "access": "item",
      "description": "The interval the value currently lives in." },
    { "name": "Target", "nickname": "T", "type": "Interval", "access": "item",
      "description": "The interval to remap the value into." },
    { "name": "Clamp", "nickname": "C", "type": "bool", "access": "item",
      "description": "When true, the factor is clamped into 0 to 1 before mapping." }
  ],

  "outputs": [
    { "name": "Mapped", "nickname": "M", "type": "double", "access": "item",
      "description": "The remapped number." },
    { "name": "Factor", "nickname": "t", "type": "double", "access": "item",
      "description": "Normalized position (0 to 1) of Value within Source." }
  ]
}
*/

using System;
using System.Collections.Generic;

using Grasshopper.Kernel;
using Rhino.Geometry;

public class Script_Instance : GH_ScriptInstance
{
  private void RunScript(double Value, Interval Source, Interval Target, bool Clamp, out object Mapped, out object Factor)
  {
    // Interval members only exist if the type hints applied — this doubles as
    // a hint-verification fixture for Script Forge.
    double t = Source.NormalizedParameterAt(Value);
    if (Clamp) t = Math.Max(0.0, Math.Min(1.0, t));
    Mapped = Target.ParameterAt(t);
    Factor = t;
  }
}
