// gh-script-kit template — copied verbatim into build/gen/ by tooling/gh_codegen.py.
//
// Marshalling between IGH_DataAccess and the plain CLR types a RunScript body
// declares. Every rule here was established empirically against a live Rhino 8
// C# Script component, because the goal is byte-for-byte behaviour parity with
// the canvas, not merely "something reasonable".
//
// Verified 2026-07-26 on Rhino 8.33 (see docs/ship-a-plugin/dotnet-build.md):
//
//   * A "No Type Hint" (object) param UNWRAPS its goo. A Panel's GH_String
//     arrives as System.String; a GH_ObjectWrapper around an IGH_DocumentObject
//     arrives as the document object itself. That is ScriptVariable() semantics,
//     so Unwrap() calls ScriptVariable() and the generated adapter must NOT hand
//     the raw goo through.
//   * Script component inputs are Optional = true by default. The generated
//     hosts set that per input, defaulting to true; without it a component with
//     an unwired input never reaches SolveInstance, so nothing that means to run
//     with an input left unwired — a Run gate, a declared "default" — could work
//     at all. A header may opt one input out with "optional": false, which is
//     asking for exactly that stop-until-wired behaviour.

using System;
using System.Collections.Generic;

using Grasshopper;
using Grasshopper.Kernel;
using Grasshopper.Kernel.Data;
using Grasshopper.Kernel.Types;

namespace GHScriptKit
{
  public static class ScriptData
  {
    /// Reproduce what a "No Type Hint" script param does to an incoming value.
    public static object Unwrap(object value)
    {
      IGH_Goo goo = value as IGH_Goo;
      if (goo == null) return value;
      try { return goo.ScriptVariable() ?? goo; }
      catch { return goo; }   // a goo whose ScriptVariable throws is still better than nothing
    }

    /// GH_Structure<TGoo> (what IGH_DataAccess hands out) -> DataTree<TValue>
    /// (what a tree-access RunScript parameter is declared as). `convert` pulls
    /// the CLR value out of one goo; nulls in the structure stay nulls.
    public static DataTree<TValue> ToTree<TGoo, TValue>(GH_Structure<TGoo> structure,
                                                        Func<TGoo, TValue> convert)
      where TGoo : IGH_Goo
    {
      DataTree<TValue> tree = new DataTree<TValue>();
      if (structure == null) return tree;

      for (int i = 0; i < structure.PathCount; i++)
      {
        GH_Path path = structure.get_Path(i);
        tree.EnsurePath(path);                 // keep empty branches — the paths are data
        List<TGoo> branch = structure.Branches[i];
        if (branch == null) continue;
        for (int j = 0; j < branch.Count; j++)
        {
          TGoo goo = branch[j];
          tree.Add(goo == null ? default(TValue) : convert(goo), path);
        }
      }
      return tree;
    }

    /// The GH_String case, which is the only one this kit's consumers need so far.
    public static DataTree<string> ToStringTree(GH_Structure<GH_String> structure)
    {
      return ToTree<GH_String, string>(structure, g => g.Value);
    }
  }
}
