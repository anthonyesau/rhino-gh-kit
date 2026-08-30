/* @component
{
  "name":        "List Stats",
  "nickname":    "Stats",
  "description": "Basic statistics over a list of numbers — count, sum, and average. Optionally round the average and ignore zero values.",
  "icon":        "icons/list-stats.svg",
  "category":    "Maths",
  "subcategory": "Util",

  "inputs": [
    { "name": "Numbers", "nickname": "N", "type": "double", "access": "list",
      "description": "The numbers to summarize." },
    { "name": "Round", "nickname": "R", "type": "int", "access": "item", "default": -1,
      "description": "Decimal places to round the average to (-1 = no rounding)." },
    { "name": "IgnoreZeros", "nickname": "Z", "type": "bool", "access": "item",
      "description": "When true, zero values are excluded from every statistic." }
  ],

  "outputs": [
    { "name": "Count", "nickname": "N", "type": "int", "access": "item",
      "description": "How many numbers were summarized." },
    { "name": "Sum", "nickname": "S", "type": "double", "access": "item",
      "description": "The total of the numbers." },
    { "name": "Average", "nickname": "A", "type": "double", "access": "item",
      "description": "The mean of the numbers." }
  ]
}
*/

// List Stats — the example component for rhino-gh-kit.
//
// Forge this file onto the canvas with the `forge-push` skill, which creates the
// component if it doesn't exist yet. The @component header above is the canonical
// metadata: one forge pass stamps Name / NickName / Description / icon and syncs
// every param, type hint, access and default from it. gh_meta.py parses the same
// header for the validation gate.
//
// The source must keep the `Script_Instance : GH_ScriptInstance` class wrapper —
// that is what binds each RunScript parameter to its canvas type hint. The param
// types are declared once, in the canvas param Converters (set from the header);
// GH rewrites the signature below to `ref object` on every solve, but the hints
// are what actually type the data. A deliberately trivial, domain-neutral
// component whose only job is to exercise the kit end-to-end.

using System;
using System.Collections.Generic;

using Grasshopper.Kernel;

public class Script_Instance : GH_ScriptInstance
{
  private void RunScript(
		List<double> Numbers,
		int Round,
		bool IgnoreZeros,
		out int Count,
		out double Sum,
		out double Average)
  {
    // Compute in local variables, then assign to the outputs once at the end.
    // On every solve GH rewrites the signature above to `ref object`, so the out
    // params are write-only object sinks — arithmetic straight on Count/Sum
    // (Count++, Sum += n) would fail to compile ('object' has no operator '++').
    int count = 0;
    double sum = 0.0;
    double average = 0.0;

    if (Numbers != null)
    {
      foreach (double n in Numbers)
      {
        if (IgnoreZeros && n == 0.0)
          continue;
        count++;
        sum += n;
      }
    }

    if (count > 0)
    {
      average = sum / count;
      if (Round >= 0)
        average = Math.Round(average, Round);
    }

    Count = count;
    Sum = sum;
    Average = average;
  }
}
