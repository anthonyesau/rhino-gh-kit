/* @component
{
  "name":        "Bad Unterminated",
  "description": "The JSON object is never closed. The matching closing brace is the header's only terminator, so an unclosed object runs off the end of the source.",

  "inputs": [
    { "name": "A", "type": "double", "access": "item",
      "description": "In." }
  ]
*/
using System; using Grasshopper.Kernel;
public class Script_Instance : GH_ScriptInstance { private void RunScript(double A, out object B){ B=A; } }
