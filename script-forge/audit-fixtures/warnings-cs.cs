/* @component
{
  "name":        "Audit Warnings",
  "description": "Has a \"double quote\" in the description to trip QUOTE WARNING.",

  "inputs": [
    { "name": "Alpha", "type": "double", "access": "item",
      "description": "Present in signature." },
    { "name": "Ghost", "type": "double", "access": "item",
      "description": "NOT in the RunScript signature (drift)." },
    { "name": "Weird", "type": "flarble", "access": "item",
      "description": "Bad type hint (should HINT WARNING)." }
  ],

  "outputs": [
    { "name": "Result", "type": "double", "access": "item",
      "description": "Output; but signature calls it Wrong (drift)." }
  ]
}
*/
using System;
using Grasshopper.Kernel;
public class Script_Instance : GH_ScriptInstance
{
  private void RunScript(double Alpha, double Weird, out object Wrong)
  {
    Wrong = Alpha + Weird;
  }
}
