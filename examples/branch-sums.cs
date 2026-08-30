/* @component
{
  "name":        "Branch Sums",
  "nickname":    "BSums",
  "description": "Sums each branch of a number tree - demonstrates tree access in the header (the RunScript signature receives a DataTree).",
  "icon":        "icons/branch-sums.svg",
  "category":    "Sets",
  "subcategory": "Tree",

  "inputs": [
    { "name": "Values", "nickname": "V", "type": "double", "access": "tree",
      "description": "A tree of numbers; each branch is summed separately." }
  ],

  "outputs": [
    { "name": "Totals", "nickname": "T", "type": "double", "access": "list",
      "description": "The sum of each branch, in branch order." },
    { "name": "Branches", "nickname": "B", "type": "int", "access": "item",
      "description": "How many branches the tree had." }
  ]
}
*/

using System;
using System.Collections.Generic;

using Grasshopper;
using Grasshopper.Kernel;

public class Script_Instance : GH_ScriptInstance
{
  private void RunScript(DataTree<double> Values, out object Totals, out object Branches)
  {
    var totals = new List<double>();
    int count = 0;

    if (Values != null)
    {
      count = Values.BranchCount;
      foreach (var branch in Values.Branches)
      {
        double sum = 0.0;
        foreach (var v in branch) sum += v;
        totals.Add(sum);
      }
    }

    Totals = totals;
    Branches = count;
  }
}
