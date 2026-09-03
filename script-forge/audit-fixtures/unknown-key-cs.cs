/* @component
{
  "name":        "Unknown Key",
  "nickanme":    "typo for nickname",
  "description": "Carries a key the grammar does not know, at the component level and inside a param. Both are ignored -- forward compatibility -- and both are warned about, because a silently dropped typo is what this fixture exists to prevent.",

  "inputs": [
    { "name": "A", "type": "double", "access": "item", "tooltip": "typo for description",
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
