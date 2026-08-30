/* @component
{
  "name":        "Bad Variable Names",
  "description": "Every way a variableName can be wrong. Rhino stores whatever you put in that slot without complaint, so --check and the forge-time warning are the only guards there are. Drift findings come with the territory here: none of these three names can appear in a legal RunScript signature, which is the point.",

  "inputs": [
    { "name": "Spaced", "variableName": "Not An Identifier", "type": "double", "access": "item",
      "description": "A variable name with spaces in it. It becomes a C# local in the generated Invoke and a live script param's NickName." },
    { "name": "Keyword", "variableName": "double", "type": "double", "access": "item",
      "description": "A C# reserved keyword. Legal as a param label, fatal as an identifier." },
    { "name": "Shared", "variableName": "Total", "type": "double", "access": "item",
      "description": "Claims the same variable name as the output below." }
  ],

  "outputs": [
    { "name": "Sum", "variableName": "Total", "type": "double", "access": "item",
      "description": "Claims the same variable name as the input above. Sharing a LABEL across sides is the feature; sharing a variable name is two locals in one generated Invoke." }
  ]
}
*/
using System; using Grasshopper.Kernel;
public class Script_Instance : GH_ScriptInstance { private void RunScript(double A, out object B){ B=A; } }
