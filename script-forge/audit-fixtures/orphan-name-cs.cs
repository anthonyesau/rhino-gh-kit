/* @component
{
  "name":        "Nonexistent ABC123",
  "description": "No on-canvas component has this name; plain name keyword must skip.",

  "inputs": [
    { "name": "A", "type": "double", "access": "item",
      "description": "In." }
  ],

  "outputs": [
    { "name": "B", "type": "double", "access": "item",
      "description": "Out." }
  ]
}
*/
using System;
using Grasshopper.Kernel;
public class Script_Instance : GH_ScriptInstance
{
  private void RunScript(double A, out object B) { B = A; }
}
