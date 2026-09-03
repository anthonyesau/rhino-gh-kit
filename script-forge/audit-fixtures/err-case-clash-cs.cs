/* @component
{
  "name":        "Case Clash",
  "Name":        "Case Clash Again",
  "description": "Two keys of one object that differ only in case. JSON permits it; the header does not, because nothing in it says which was meant.",

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
