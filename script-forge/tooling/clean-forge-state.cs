// clean-forge-state.cs — strip Script Forge's saved state out of a .gh document.
//
// WHY: up to 0.3.1-beta, Script Forge remembered what it had last forged in the
// GH document's value table — an applied key, a result guid, a settled-text
// hash and a Force flag per (forge instance, branch path, target slot), plus a
// '.paths' ledger listing them. Identity is declared rather than remembered from
// 0.4.0-beta on, so the forge writes nothing to the document and reads nothing
// back, and every one of those entries is dead weight a saved .gh carries
// forever — a canvas forged at for a while can hold hundreds. This is the
// one-off migration that removes them.
//
// It is safe and total: the whole "ScriptForge." namespace is Forge's own, and
// no version of Forge from 0.4.0 on ever reads it. Nothing else in the value
// table is touched.
//
// HOW TO RUN: paste into the Rhino MCP `run_csharp` tool, which executes it
// against the running Rhino — the ACTIVE Grasshopper document is the one
// cleaned, so open the .gh you mean first.
//   1. Run once with DRY_RUN = true to see the count and a sample.
//   2. Set DRY_RUN = false and run again to apply.
//   3. Save the .gh (Ctrl+S) — the value table only persists on save.
//
// Per-document: each .gh carries its own residue, so run it in each file that
// was ever forged at and saved by 0.3.1-beta or earlier. Documents created
// under 0.4.0-beta never need it.

using System;
using System.Collections.Generic;
using System.Linq;
using Grasshopper;
using Grasshopper.Kernel;

const bool DRY_RUN = true;   // true = preview only; false = actually delete

var ghDoc = Grasshopper.Instances.ActiveCanvas?.Document;
if (ghDoc == null) { Console.WriteLine("no active Grasshopper document"); return; }

var vt = ghDoc.ValueTable;
// Snapshot the names before mutating — don't enumerate and delete at once.
var stale = vt.EntryNames().Where(n => n.StartsWith("ScriptForge.")).ToList();

Console.WriteLine((DRY_RUN ? "[DRY RUN] " : "") + "ScriptForge.* entries: " + stale.Count
  + " of " + vt.Count + " in " + (ghDoc.FilePath ?? "<unsaved document>"));
foreach (var n in stale.Take(12)) Console.WriteLine("  " + n);
if (stale.Count > 12) Console.WriteLine("  … and " + (stale.Count - 12) + " more");

if (!DRY_RUN)
{
  foreach (var n in stale) vt.DeleteValue(n);
  ghDoc.Modified();
  Console.WriteLine("--- deleted " + stale.Count + " entr" + (stale.Count == 1 ? "y" : "ies")
    + ". Save the .gh (Ctrl+S) to persist.");
}
else
{
  Console.WriteLine("--- set DRY_RUN = false and re-run to apply, then save the .gh.");
}
