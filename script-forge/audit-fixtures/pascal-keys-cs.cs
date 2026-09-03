/* @component
{
  "Name":        "Pascal Keys",
  "NickName":    "Pascal",
  "Description": "Every key in PascalCase. Keys are matched case-insensitively, so the documented spelling is canonical rather than required.",

  "Inputs": [
    { "Name": "Value", "VariableName": "InValue", "Type": "double", "Access": "item",
      "Description": "Declared under PascalCase param keys." }
  ],

  "Outputs": [
    { "Name": "Result", "VariableName": "OutResult", "Type": "double", "Access": "item",
      "Description": "Declared under PascalCase param keys." }
  ]
}
*/
using System;
using Grasshopper.Kernel;
public class Script_Instance : GH_ScriptInstance
{
  private void RunScript(double InValue, out object OutResult) { OutResult = InValue; }
}
