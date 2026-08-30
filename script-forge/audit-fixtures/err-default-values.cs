/* @component
{
  "name":        "Bad Default Values",
  "nickname":    "BadDef",
  "description": "Every way a default or an optional can be wrong. gh_meta --check and a live forge must report the same seven findings, in the same order, and the forge must leave every rejected param's persistent data empty.",
  "category":    "Params",
  "subcategory": "Util",
  "language":    "csharp",

  "inputs": [
    { "name": "WrongKind", "type": "bool", "access": "item", "default": "yes",
      "description": "A string default on a bool param." },
    { "name": "Truncating", "type": "int", "access": "item", "default": 2.5,
      "description": "A fractional default on an int param." },
    { "name": "BoolAsInt", "type": "int", "access": "item", "default": true,
      "description": "true is not a whole number - and in Python isinstance(True, int) is True, so this is the guard that check exists for." },
    { "name": "NotDefaultable", "type": "Point3d", "access": "item", "default": 0,
      "description": "A hint with no JSON scalar spelling." },
    { "name": "Composite", "type": "double", "access": "item", "default": [0, 0, 1],
      "description": "An array default - not a JSON scalar at all. The forge must warn, not silently ignore it." }
  ],

  "outputs": [
    { "name": "Echo", "type": "string", "access": "item", "default": "nope",
      "description": "An output cannot carry a default; nothing is ever collected into it." },
    { "name": "Strict", "type": "string", "access": "item", "optional": false,
      "description": "An output cannot be non-optional either; RegisterOutputParams has no Optional pass to mirror it." }
  ]
}
*/

using System;
using System.Collections.Generic;
using System.Linq;

using Grasshopper;
using Grasshopper.Kernel;

public class Script_Instance : GH_ScriptInstance
{
  private void RunScript(bool WrongKind, int Truncating, int BoolAsInt, Rhino.Geometry.Point3d NotDefaultable, double Composite, out string Echo, out string Strict)
  {
    Echo = "";
    Strict = "";
  }
}
