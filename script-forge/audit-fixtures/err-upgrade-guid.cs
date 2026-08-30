/* @component
{
  "name":          "Bad Upgrade Guid",
  "description":   "componentGuid and upgradeFrom are compile-path-only keys. Script Forge's own parser never even reads either into HeaderMeta, so a live forge accepts anything here without complaint - gh_meta --check is the ONLY guard on this pair, not a stricter twin of one that also warns. Pins that asymmetry: one malformed guid string and one upgradeFrom naming this component's own componentGuid.",
  "componentGuid": "aaaaaaaa-bbbb-cccc-dddd-eeeeeeeeeeee",
  "upgradeFrom":   ["not-a-guid", "aaaaaaaa-bbbb-cccc-dddd-eeeeeeeeeeee"],

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
using System; using Grasshopper.Kernel;
public class Script_Instance : GH_ScriptInstance { private void RunScript(double A, out object B){ B=A; } }
