/* @component
{
  "name":        "Display Name Grammar",
  "description": "Exercises the optional per-param display name and the pipe-in-description case. Keys goes in and an output named Keys with variableName OutKeys comes out, so a compiled build labels both params Keys while the C# identifiers stay distinct; Notes carries an early pipe in its description, which must survive verbatim.",

  "inputs": [
    { "name": "Keys", "type": "string", "access": "list",
      "description": "Entry names. Shares a display label with the output, which is the whole reason the display name exists." },
    { "name": "Notes", "type": "string", "access": "item",
      "description": "A description opening with a pipe-separated run: list | of | things. Pipes carry no meaning in a JSON header and must come through untouched." }
  ],

  "outputs": [
    { "name": "Keys", "variableName": "OutKeys", "type": "string", "access": "list",
      "description": "Echo of Keys. Variable name OutKeys, display name Keys." },
    { "name": "Total", "variableName": "Count", "type": "int", "access": "item",
      "description": "How many keys came in. Display name differs from the variable name on an output with no counterpart input." }
  ]
}
*/
using System;
using System.Collections.Generic;
using Grasshopper.Kernel;

public class Script_Instance : GH_ScriptInstance
{
  private void RunScript(List<string> Keys, string Notes, out List<string> OutKeys, out int Count)
  {
    var echo = new List<string>(Keys ?? new List<string>());
    OutKeys = echo;
    Count = echo.Count;
  }
}
