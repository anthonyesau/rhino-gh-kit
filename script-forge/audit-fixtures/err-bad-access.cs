/* @component
{
  "name":        "Bad Access",
  "description": "Param has an invalid access value.",

  "inputs": [
    { "name": "A", "type": "double", "access": "itemx",
      "description": "Bad access value." }
  ],

  "outputs": [
    { "name": "B", "type": "double", "access": "item",
      "description": "Out." }
  ]
}
*/
using System; using Grasshopper.Kernel;
public class Script_Instance : GH_ScriptInstance { private void RunScript(double A, out object B){ B=A; } }
