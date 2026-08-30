/* @component
{
  "name":        "Duplicate Display Name",
  "description": "Two outputs claiming the same display name, which would compile to a component with two identically-labelled params. Sharing a display name ACROSS sides is the feature; sharing it within one side is the error.",

  "inputs": [
    { "name": "Keys", "type": "string", "access": "list",
      "description": "Entry names." }
  ],

  "outputs": [
    { "name": "Keys", "variableName": "OutKeys", "type": "string", "access": "list",
      "description": "Legitimately shares its label with the input above." },
    { "name": "Keys", "variableName": "Extra", "type": "string", "access": "list",
      "description": "Illegitimately shares its label with the output above." }
  ]
}
*/
using System;
using System.Collections.Generic;
using Grasshopper.Kernel;

public class Script_Instance : GH_ScriptInstance
{
  private void RunScript(List<string> Keys, out List<string> OutKeys, out List<string> Extra)
  {
    OutKeys = Keys;
    Extra = Keys;
  }
}
