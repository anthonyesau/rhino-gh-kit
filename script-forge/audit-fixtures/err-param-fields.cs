/* @component
{
  "name":        "Bad Param Fields",
  "description": "A param object missing its required type key. Every param field is a named key, so the failure has to name the key that is absent.",

  "inputs": [
    { "name": "A", "access": "item",
      "description": "No type." }
  ],

  "outputs": [
    { "name": "B", "type": "double", "access": "item",
      "description": "Out." }
  ]
}
*/
using System; using Grasshopper.Kernel;
public class Script_Instance : GH_ScriptInstance { private void RunScript(double A, out object B){ B=A; } }
