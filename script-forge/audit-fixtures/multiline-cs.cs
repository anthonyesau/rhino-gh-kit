/* @component
{
  "name":        "Audit Multiline",
  "description": "Line one.\nLine two.\nLine three.",

  "inputs": [
    { "name": "X", "type": "double", "access": "item",
      "description": "First line of tip.\nSecond line of tip." }
  ],

  "outputs": [
    { "name": "Y", "type": "double", "access": "item",
      "description": "Just one line." }
  ]
}
*/
using System;
using Grasshopper.Kernel;
public class Script_Instance : GH_ScriptInstance
{
  private void RunScript(double X, out object Y) { Y = X; }
}
