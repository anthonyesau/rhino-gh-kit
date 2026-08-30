/* @component
{
  "name":        "Zzz Unique Widget",
  "nickname":    "ZUW",
  "description": "Uniquely-named source with no on-canvas match; tests name+create.",

  "inputs": [
    { "name": "N", "type": "int", "access": "item",
      "description": "A number." }
  ],

  "outputs": [
    { "name": "M", "type": "int", "access": "item",
      "description": "N doubled." }
  ]
}
*/
using System;
using Grasshopper.Kernel;
public class Script_Instance : GH_ScriptInstance
{
  private void RunScript(int N, out object M) { M = N * 2; }
}
