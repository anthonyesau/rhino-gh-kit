/* @component
{
  "name":          "Audit Pinned",
  "nickname":      "APin",
  "description":   "Pinned via header guid; forge updates this instance without a Target wire.",
  "instanceGuid":  "11112222-3333-4444-5555-666677778888",

  "inputs": [
    { "name": "P", "type": "double", "access": "item",
      "description": "Input." }
  ],

  "outputs": [
    { "name": "Q", "type": "double", "access": "item",
      "description": "Output." }
  ]
}
*/
using System;
using Grasshopper.Kernel;
public class Script_Instance : GH_ScriptInstance
{
  private void RunScript(double P, out object Q) { Q = P + 2.0; }
}
