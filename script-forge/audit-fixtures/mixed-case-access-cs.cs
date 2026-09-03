/* @component
{
  "name":        "Mixed Case Access",
  "description": "Access values spelled in mixed case. Matching is case-insensitive; the value is stored canonical.",

  "inputs": [
    { "name": "Branches", "type": "double", "access": "Tree",
      "description": "Tree access, written Tree." },
    { "name": "Values", "type": "double", "access": "LIST",
      "description": "List access, written LIST." },
    { "name": "Factor", "type": "double", "access": "Item",
      "description": "Item access, written Item." }
  ],

  "outputs": [
    { "name": "Scaled", "type": "double", "access": "TREE",
      "description": "One branch out per branch in." }
  ]
}
*/
using System;
using System.Collections.Generic;
using Grasshopper;
using Grasshopper.Kernel;
using Grasshopper.Kernel.Data;

public class Script_Instance : GH_ScriptInstance
{
  // Declared `out object` because a live canvas rewrites the signature to that
  // on every solve; the tree is built in a local and assigned once at the end.
  private void RunScript(DataTree<double> Branches, List<double> Values, double Factor, out object Scaled)
  {
    var scaled = new DataTree<double>();
    if (Branches != null)
      for (int i = 0; i < Branches.BranchCount; i++)
      {
        var path = Branches.Path(i);
        var branch = Branches.Branch(i);
        scaled.EnsurePath(path);
        for (int j = 0; j < branch.Count; j++)
        {
          double offset = (Values != null && Values.Count > 0) ? Values[j % Values.Count] : 0.0;
          scaled.Add(branch[j] * Factor + offset, path);
        }
      }
    Scaled = scaled;
  }
}
