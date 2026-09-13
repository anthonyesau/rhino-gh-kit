/* @component
{
  "name":        "Audit Multiline Array",
  "description": [
    "Line one.",
    "Line two.",
    "Line three."
  ],

  "inputs": [
    { "name": "X", "type": "double", "access": "item",
      "description": ["First line of tip.", "Second line of tip."] }
  ],

  "outputs": [
    { "name": "Y", "type": "double", "access": "item",
      "description": "Just one line." }
  ]
}
*/
// The array spelling of a prose value: one element per line, joined with a
// newline. Pinned against multiline-cs.cs, which says the same thing with \n
// escapes -- both parsers must resolve the two to the identical string.
using System;
using Grasshopper.Kernel;
public class Script_Instance : GH_ScriptInstance
{
  private void RunScript(double X, out object Y) { Y = X; }
}
