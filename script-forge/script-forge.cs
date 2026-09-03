/* @component
{
  "name":          "Script Forge",
  "nickname":      "Forge",
  "description":   "Builds or updates script components from source text or source files.",
  "icon":          "script-forge.svg",
  "instanceGuid":  "41822538-1827-4da2-bf84-58074c49b3ad",
  "componentGuid": "41822538-1827-4da2-bf84-58074c49b3ad",
  "category":      "Params",
  "subcategory":   "Util",
  "exposure":      "level1",

  "inputs": [
    { "name": "Source", "nickname": "S", "type": "string", "access": "tree",
      "description": "Source text or file path. One script per branch.\n• Source text: a multiline string, or a list of lines (e.g. Read File output).\n• File path: *.cs or *.py, absolute or relative to the saved .gh file." },
    { "name": "Target", "nickname": "T", "type": "object", "access": "tree",
      "description": "Components to update, or a header-matching keyword. One branch per source.\n• Unwired: match the header's instanceGuid, else every component whose Name matches the header, else forge new.\n• A component item: instance guid, guid string, or component goo.\n• 'name': update every component whose Name matches the source header.\n• 'nickname': the same, matching NickName.\n• 'name+create' / 'nickname+create': as above, but forge a new component if none match.\n• A null or blank item: always forge a new component, even when the header pins an instanceGuid.\n• Wired but zero items for a branch: skip it — nothing forged.\nKeywords are case-insensitive and must be the whole item. No match skips the branch." },
    { "name": "Run", "nickname": "R", "type": "bool", "access": "item",
      "description": "Forges on the RISING edge — the moment Run goes false to true. A momentary button is the natural fit; a toggle forges when switched on, and not again while it stays on. What is wired here is never inspected, so a relay, a Gate, an expression or a script output all read alike. While Run stays true, file-path sources are watched on disk and re-forge automatically when the file is edited — no Read File rig." }
  ],

  "outputs": [
    { "name": "Success", "nickname": "OK", "type": "bool", "access": "tree",
      "description": "True or false per target slot — whether the forge's own synchronous pass for that slot succeeded. A target script's own runtime error is not a forge failure. Skipped branches stay empty." },
    { "name": "Log", "nickname": "L", "type": "string", "access": "tree",
      "description": "Step-by-step report per branch, sectioned per target: header parse, target resolution, param sync, lost wires, and the push. Stamping runs after the target compiles, so its outcome is not here — a stamping failure raises this component's own error bubble instead." }
  ]
}
*/

// Param `nickname`s reach the COMPILED build only — a script component's drawn
// NickName is its `variableName`, so the canvas forge keeps labelling these
// params Source / Target / Run / Success / Log while the .gha draws
// S / T / R / OK / L.
//
// Script Forge — a script component that forges other script components.
// Standalone: everything runs on Grasshopper/RhinoCodePluginGH APIs (the
// RhinoCodePluginGH types are reached via reflection so this source has no
// compile-time references beyond stock Grasshopper).
//
// The @component header format (first comment block of the source) is one JSON
// object; its closing brace ends the header, so there is no terminator keyword:
//
//   /* @component            ("""@component or # @component in Python)
//   {
//     "name":         "My Component",   (required)
//     "nickname":     "Mine",           (optional, defaults to name)
//     "description":  "Hover tooltip.", (required; no double quotes)
//     "icon":         "icons/my-comp.svg",   (optional; .svg/.png path relative
//                                             to the .gh folder, or
//                                             base64:<png bytes>)
//     "language":     "csharp",         (or "python"; usually auto-detected)
//     "instanceGuid": "<guid>",         (optional; pins the target component)
//
//     "inputs":  [ { "name": "X", "type": "double", "access": "item",
//                    "description": "Tooltip for the param." } ],
//     "outputs": [ { "name": "Y", "variableName": "OutY", "type": "double",
//                    "access": "item", "description": "…" } ]
//   }
//
// `name` is the param's PrettyName — the tooltip title. `variableName` is the
// C# identifier, which on a live script component IS what GH draws (NickName);
// it defaults to `name`. An input may also carry "optional": false and a
// "default" (bool/int/double/string). Escapes are JSON's, so a "\n" in a
// description is a real newline by the time the parser hands it over. Full
// reference: docs/write-scripts/header-reference.md.
//
// The three-step solve choreography is load-bearing: a component must not
// mutate the document during its own solve, and a source push (set_Text)
// resets every param Description on the solve that follows it. So solution A
// (this RunScript) only plans and schedules; callback 1 mutates (add, param
// sync, hints, source push — bracketed by the marshalling capture/restore,
// belt-and-braces against set_Text clearing a Python component's Marshal
// toggles; see the note on CaptureMarshalling) and
// expires the target; callback 2 runs after the target has compiled, restamps
// identity + tooltips + icon, and expires this component so the Log output
// picks up the final report.
//
// Source is a tree: each branch is one script — the source text itself, a
// single .cs/.py file path, or a plain LIST of paths, which expands into one
// unit per path exactly as if grafted (unit path = branch path + item index,
// so the two shapes address the same slots). LoadSourceIfPath reads path units
// so everything downstream (keyword matching, the identity ladder, the push)
// sees the file's content. A single forge can therefore fan out
// to N components — and a Target branch can itself hold several components,
// fanning one script out to every instance of it on the canvas. RunScript
// runs ONCE per solve (no implicit iteration) and loops the branches (and
// each branch's targets) itself — all mutations coalesce onto one scheduled
// solution and all stampings onto the next.
//
// Two rules govern the whole component:
//
//   IDENTITY IS DECLARED, NEVER REMEMBERED.
//   RUN PUSHES ON THE RISING EDGE, AND WATCHES WHILE IT IS HELD.
//
// Nothing is stored in the .gh — no applied key, no result guid, no settled
// text, no change detection — so the forge is a command rather than a
// function: a press always pushes, and every target is re-resolved from
// scratch. The identity ladder, walked fresh on every pass, is
//
//   wired Target -> header instanceGuid -> header `name` match -> create new
//
// and its third rung is what makes a press idempotent: forge twice with Target
// unwired and you get ONE component, because the second pass finds the first
// by the very name its own header stamped on it. A wired Target item that is
// explicitly empty means "always create new" — the escape hatch for a
// deliberate second copy — and a wired Target that resolves to nothing for a
// branch skips it. The only state is in memory and dies with the session: the
// last Run value per instance (the edge detector), and the busy flag plus the
// last report per (instance guid, branch path, target slot).

using System;
using System.Collections;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text.Json;
using System.Text.RegularExpressions;

using Grasshopper;
using Grasshopper.Kernel;
using Grasshopper.Kernel.Data;
using Grasshopper.Kernel.Types;

public class Script_Instance : GH_ScriptInstance
{
  // Session state — all of it in memory, none of it in the .gh. Keyed per
  // (instance guid, branch path, target slot): several forge components with
  // identical source share one compiled assembly, so bare statics would
  // crosstalk between instances — one instance fans out to one component per
  // Source branch, so instance-only keys would crosstalk between branches —
  // and one branch fans out to one component per Target item, so path-only
  // keys would crosstalk between a branch's targets.
  //
  // _reports is what Log shows when the forge is not pushing, and it has to
  // outlive the push: a momentary button springs back and re-solves this
  // component with Run=false within the same interaction, and the Log would
  // blank the instant it did. It carries the synchronous pass plus the
  // mutation that follows it; what happens after the target COMPILES cannot
  // reach it, because the forge no longer expires itself to fetch it back.
  static readonly Dictionary<string, List<string>> _reports = new Dictionary<string, List<string>>();
  static readonly HashSet<string> _busyIds = new HashSet<string>();
  static readonly List<RectangleF> _claims = new List<RectangleF>();

  // The edge detector: the Run value this instance last saw. A MISSING key
  // means unset, and the first value observed after a document load or a
  // recompile becomes the baseline rather than an edge — so opening a document
  // with a toggle left on arms the watchers and pushes nothing. Deliberately
  // never persisted: a .gh that remembered it was mid-press would push on open.
  static readonly Dictionary<Guid, bool> _lastRun = new Dictionary<Guid, bool>();

  const string CsComponentType = "RhinoCodePluginGH.Components.CSharpComponent";
  const string PyComponentType = "RhinoCodePluginGH.Components.Python3Component";

  // A freshly created Python 3 component's engine appears to attach its script
  // module asynchronously after set_Text returns: forging a new Python 3
  // component and expiring it within the same ~10ms tick hard-crashed Rhino
  // (SIGABRT inside Python.Runtime.ModuleObject.tp_setattro racing
  // ManagedType.GetManagedObject). No readiness flag was found to poll instead
  // (reflection turned up Context.IsInitialized, always true immediately, and
  // Context.LanguagesReady(), which never completes outside an actual solve) —
  // this is empirical insurance against the race window, not a guaranteed fix.
  const int FreshPythonSettleMs = 250;

  // The one language → stock-component-class mapping: the keyword matcher
  // (MatchTargetsByName) must select exactly the type Forge will accept.
  static string ComponentTypeFor(bool isPython)
  {
    return isPython ? PyComponentType : CsComponentType;
  }

  // Slot t is a target's position within its branch; the slot name appends #t.
  // GH_Path.ToString() never contains '#', so the two halves stay legible in a
  // log line. Nothing is written to the document any more, so these names key
  // the in-memory tables only.
  static string SlotName(GH_Path path, int t) { return path.ToString() + "#" + t; }
  string Slot(GH_Path path, int t) { return Component.InstanceGuid + "|" + SlotName(path, t); }

  // Outputs are declared with their real types, not the `out object` the canvas
  // rewrites them to on every solve. On canvas the declaration is decorative
  // either way; the compiled build derives the DA calls from the @component
  // header, so gh_codegen.py treats any header/signature disagreement as fatal.
  private void RunScript(DataTree<string> Source, DataTree<object> Target, bool Run,
                         out DataTree<bool> Success, out DataTree<string> Log)
  {
    // Success mirrors the Log's per-target slots; false = that slot's report
    // carries a forge error (same IsOwnError test the error re-raise uses).
    var oks = new DataTree<bool>();
    var logs = new DataTree<string>();
    var doc = Component.OnPingDocument();
    int branches = Source == null ? 0 : Source.BranchCount;

    // Run is read twice out of the same value, and what is WIRED to it is
    // never inspected — a button, a toggle, a relay, a Gate, an expression, a
    // script output and internalised data all read alike. As a LEVEL it arms
    // the file watchers; as an EDGE (false -> true) it pushes. _lastRun
    // starting UNSET is what keeps a document that opens with Run already true
    // from pushing on load.
    var me = Component.InstanceGuid;
    bool prevRun;
    bool hadPrev = _lastRun.TryGetValue(me, out prevRun);
    bool rising = Run && hadPrev && !prevRun;
    _lastRun[me] = Run;

    // The third trigger: a watched source file changed on disk. The watcher
    // records WHICH paths changed and expires this component; that solve is
    // not a rising edge, so only the units whose own file changed push. Drained
    // unconditionally — an event that arrived as Run was switched off is
    // discarded here rather than left to fire on some later solve — but only
    // honoured while Run is still true, since Run false means disarmed.
    var changed = DrainPending(me);
    if (!Run) changed.Clear();

    if (branches == 0)
    {
      var p0 = new GH_Path(0);
      if (!Run)
      {
        logs.Add("Run is false — press Run (a button) or switch it on (a toggle) to forge.", p0);
      }
      else
      {
        logs.Add("ERROR: Source is empty — feed it the text of one or more .cs or .py component sources.", p0);
        Component.AddRuntimeMessage(GH_RuntimeMessageLevel.Error,
          "Source is empty — feed it the text of one or more .cs or .py component sources.");
      }
    }

    // Whether Target is WIRED decides between the two no-target meanings:
    // unwired -> walk the identity ladder; wired but empty for a branch ->
    // skip it. Looked up by param NAME, never index (Out can be hidden, which
    // shifts every index) and never NickName: Name is the durable identifier,
    // while a nickname is a label the user can retype, so a NickName lookup can
    // silently miss and turn every wired Target into a new component.
    bool targetWired = false;
    foreach (var p in Component.Params.Input)
      if (p.Name == "Target") { targetWired = p.SourceCount > 0; break; }

    var seenTargets = new HashSet<Guid>();

    // A branch that is a plain LIST of file paths (several items, or one
    // multiline panel, every non-blank line a path) expands into one unit per
    // path — each unit forging its own script, exactly as if the list were
    // grafted. Unit paths append the item index to the branch path, so a
    // grafted rig and a list address the same slots item-for-item. Every other
    // branch stays one unit. Decided on text shape alone (no file IO), so a
    // solve that pushes nothing keys its slots exactly as a pushing one does.
    var units = new List<SourceUnit>();
    for (int bi = 0; bi < branches; bi++)
    {
      var bpath = Source.Paths[bi];
      string joined = string.Join("\n", Source.Branches[bi]).TrimStart('\uFEFF');
      var pathList = PathLines(joined);
      if (pathList != null)
        for (int i = 0; i < pathList.Count; i++)
          units.Add(new SourceUnit { Path = bpath.AppendElement(i), Text = pathList[i] });
      else
        units.Add(new SourceUnit { Path = bpath, Text = joined, Items = Source.Branches[bi] });
    }
    // Resolved once per unit and reused by both the watcher registry and the
    // watcher-event trigger, so the set watched and the set a file event can
    // fire is the same set by construction. Skipped entirely when Run is false
    // and nothing is pending: the watchers are about to be torn down and no
    // unit can be triggered, so the File.Exists behind it would be pure cost.
    if (Run)
      foreach (var u in units) u.WatchPath = WatchPathOf(u.Text, doc);

    // One canvas pass instead of one doc.Objects scan per branch — the
    // identity ladder's name match and every by-guid lookup read it. Built
    // only on a pass that can actually push: with change detection gone, a
    // forge that is not pushing reads nothing off the canvas at all.
    Dictionary<Guid, IGH_DocumentObject> liveObjects = null;
    if (doc != null && (rising || changed.Count > 0))
    {
      liveObjects = new Dictionary<Guid, IGH_DocumentObject>();
      foreach (var o in doc.Objects) liveObjects[o.InstanceGuid] = o;
    }
    for (int ui = 0; ui < units.Count; ui++)
    {
      var path = units[ui].Path;
      var log = new List<string>();
      var branchOk = new List<bool>();

      // The three triggers, resolved per unit. A rising edge on Run pushes
      // every unit; a watcher event pushes only the unit whose own file
      // changed; anything else — Run held true, Run false, a solve some
      // upstream change caused — pushes nothing and replays what the slot last
      // did. There is no fourth case: with no change detection, "should this
      // push?" is answered by the trigger alone, never by comparing anything.
      bool push = rising
        || (changed.Count > 0 && units[ui].WatchPath != null && changed.Contains(units[ui].WatchPath));

      try
      {
        string text = units[ui].Text;

        if (!push)
        {
          log.Add(Run
            ? "Run is armed — file sources are watched; take Run false and true again to forge."
            : "Run is false — press Run (a button) or switch it on (a toggle) to forge.");
          ReplayReports(path, log, branchOk);
        }
        else if (text.Trim().Length == 0)
        {
          throw new Exception("Source branch " + path + " is empty — feed it the text of a .cs or .py component source.");
        }
        else
        {
          text = LoadSourceIfPath(text, doc, path, log);
          if (units[ui].Items != null) WarnMultiSourceBranch(units[ui].Items, path, log);
          bool skip;
          var targets = ResolveTargetsForBranch(Target, targetWired, path, ui, units.Count,
            text, liveObjects, log, out skip);
          if (skip)
          {
            // Store the skip note as this branch's last-run report so a
            // momentary button still shows it once it springs back.
            _reports[Slot(path, 0)] = new List<string>(log);
            TrimReports(path, 1);
          }
          else
          {
            // Branch-level lines — the source-file note, the multi-source
            // warning, everything ResolveTargetsForBranch wrote about the
            // identity ladder or a keyword expansion, and the per-target
            // separators — are written into the BRANCH log, but a stored report
            // is per SLOT. So hand each slot the branch lines that preceded it,
            // and let ForgeSlot's own lines flow back into the branch log.
            // Without this the live Log showed them and the replay did not —
            // which, with a momentary button, means never: a keyword that
            // matched nothing would report no reason, reading exactly like a
            // dead Target wire. `handed` keeps a multi-target branch from
            // repeating the shared prefix once per slot.
            int handed = 0;
            for (int t = 0; t < targets.Count; t++)
            {
              if (targets.Count > 1)
                log.Add(string.Format("— target {0} of {1}: {2} —", t + 1, targets.Count,
                  targets[t] == Guid.Empty ? "new component" : targets[t].ToString()));
              int prefix = log.Count - handed;
              var slotLog = log.GetRange(handed, prefix);
              ForgeSlot(liveObjects, seenTargets, text, targets[t], path, t, slotLog);
              // Read over the whole slot log, prefix included, exactly as the
              // replay reads the stored report. No branch line is an own error,
              // so the two always agree.
              branchOk.Add(!slotLog.Any(IsOwnError));
              // Store the SYNCHRONOUS report now. The mutation callback replaces
              // it with a fuller one a moment later, but a momentary button has
              // already sprung back and re-solved by then, and this is what that
              // solve reads.
              _reports[Slot(path, t)] = new List<string>(slotLog);
              log.AddRange(slotLog.GetRange(prefix, slotLog.Count - prefix));
              handed = log.Count;
            }
            TrimReports(path, targets.Count);
          }
        }
      }
      catch (Exception ex)
      {
        log.Add("ERROR: " + ex.Message);
        Component.AddRuntimeMessage(GH_RuntimeMessageLevel.Error, path + ": " + ex.Message);
        // Store the failure as this branch's last-run report. A synchronous
        // throw here (e.g. an empty source branch) never reaches the slot loop
        // that normally records one, so without this the Log would fall back to
        // the previous — stale, often successful — report the instant Run goes
        // false, which is why a momentary button made the error flash and
        // vanish.
        _reports[Slot(path, 0)] = new List<string>(log);
        TrimReports(path, 1);
        branchOk.Clear();
        branchOk.Add(false);
      }

      oks.EnsurePath(path);
      foreach (var b in branchOk) oks.Add(b, path);
      logs.EnsurePath(path);
      logs.AddRange(log, path);
    }

    // While Run is true, watch every file-path Source on disk so an external
    // edit re-forges with no upstream Read File. The watcher notes which path
    // changed and expires this component; the ordinary solve re-reads that file
    // and pushes just its unit. With change detection gone the recorded path IS
    // the trigger, which is what makes the debounce load-bearing: one save
    // fires several events, and each one that survived would be its own push.
    // Run false (or no path sources) tears the watchers down. Runs on every
    // solve — including Run=false — so flipping Run off disarms.
    SyncWatchers(Run, units, doc);

    Success = oks;
    Log = logs;
  }

  // One (branch, target) slot: forge it. No no-op branch and no persisted
  // state — a press is a command, so it always pushes. Catches its own
  // failures so one bad target does not abort the branch's remaining targets;
  // the caller stores this log as the slot's last-run report either way.
  void ForgeSlot(Dictionary<Guid, IGH_DocumentObject> liveObjects, HashSet<Guid> seenTargets,
    string text, Guid targetGuid, GH_Path path, int t, List<string> log)
  {
    try
    {
      if (targetGuid != Guid.Empty && !seenTargets.Add(targetGuid))
        throw new Exception("target " + targetGuid + " is already claimed by an earlier branch or target slot — skipping");
      // A slot whose previous push has not finished stamping yet. Pushing over
      // it would race the scheduled callbacks that are still holding the
      // component; the next press picks it up.
      if (_busyIds.Contains(Slot(path, t)))
      {
        log.Add("forge in progress…");
        return;
      }
      Forge(liveObjects, text, targetGuid, path, t, log);
    }
    catch (Exception ex)
    {
      log.Add("ERROR: " + ex.Message);
      Component.AddRuntimeMessage(GH_RuntimeMessageLevel.Error,
        SlotName(path, t) + ": " + ex.Message);
    }
  }

  // What Log shows when this pass pushes nothing: each slot's stored report,
  // replayed in slot order. The forge does not re-solve itself after a push,
  // so this is also how a momentary button's own release renders the press it
  // just made.
  void ReplayReports(GH_Path path, List<string> log, List<bool> branchOk)
  {
    for (int t = 0; ; t++)
    {
      List<string> last;
      if (!_reports.TryGetValue(Slot(path, t), out last) || last.Count == 0) break;
      log.Add(t == 0 ? "— last run —" : "— last run, target " + (t + 1) + " —");
      log.AddRange(last);
      branchOk.Add(!last.Any(IsOwnError));
      // A momentary button clears the runtime bubble the instant it springs
      // back, so re-raise the last run's error while at rest — otherwise a
      // failed forge only shows red while the button is physically held. Only
      // the FORGE's own failures re-raise.
      var err = last.FirstOrDefault(IsOwnError);
      if (err != null)
        Component.AddRuntimeMessage(GH_RuntimeMessageLevel.Error, path + ": " + err);
    }
  }

  // The in-memory counterpart of the ValueTable prune 0.4.0 deleted: a branch
  // whose target count shrank leaves reports for slots it no longer has, and
  // ReplayReports walks t upward until it finds a gap. Drop the tail.
  void TrimReports(GH_Path path, int keep)
  {
    for (int t = keep; _reports.Remove(Slot(path, t)); t++) { }
  }

  // The two scheduled callbacks run after this component's own solve has
  // ended, so nothing they learn can reach Log — and expiring the forge to
  // carry it back is exactly the self-expire loop 0.4.0 removed. Raise it on
  // the forge's own bubble instead: GH clears a component's runtime messages
  // at the start of its next solve, which is precisely the lifetime wanted,
  // and ExpireLayout plus a canvas refresh paints the balloon WITHOUT
  // recomputing anything.
  static void ReportAsync(IGH_Component self, GH_RuntimeMessageLevel level, string msg)
  {
    try
    {
      self.AddRuntimeMessage(level, msg);
      self.Attributes.ExpireLayout();
      Grasshopper.Instances.ActiveCanvas?.Refresh();
    }
    catch { /* the canvas may be gone; the message is best-effort */ }
  }

  // The forge's own failure lines. MUTATION and STAMP errors happen after this
  // component's solve has ended, so they reach the error bubble rather than a
  // report — but they share the prefixes, and the mutation report IS stored,
  // so all three stay listed.
  static bool IsOwnError(string line)
  {
    return line.StartsWith("ERROR:") || line.StartsWith("MUTATION ERROR:") || line.StartsWith("STAMP ERROR:");
  }

  // A Source branch may hold FILE PATHS instead of source text. Detection is
  // by shape: real source is multiline and never all path-looking, so a lone
  // line ending in .cs or .py is a path, and a branch whose every non-blank
  // line is one is a path LIST (expanded into per-path units before the
  // branch loop — see SourceUnit). Relative paths resolve against the saved
  // .gh folder (the same anchor icons use). The loaded content replaces text
  // for everything downstream — keyword matching, the identity ladder, the
  // push. While Run is true a FileSystemWatcher on each path's folder expires
  // this component when the file is edited on disk, so it re-forges
  // automatically with no Read File rig (see SyncWatchers). Reading happens
  // only on a pass that actually pushes, so path sources are also cheaper at
  // rest than a native Read File, which re-reads on every canvas solve.
  class SourceUnit
  {
    public GH_Path Path;      // branch path, plus the item index when expanded
    public string Text;       // source text, or a single path line
    public IList<string> Items; // original branch items; null for expanded units
    public string WatchPath;  // absolute path when Text names a readable file, else null
  }

  // A unit's watch path: the absolute file it would read, resolved exactly as
  // LoadSourceIfPath resolves it, or null when the unit is inline source. The
  // single definition behind both halves of the watch story — what SyncWatchers
  // registers, and what a file event has to match for its unit to push — so
  // the two cannot drift apart.
  static string WatchPathOf(string text, GH_Document doc)
  {
    if (doc == null) return null;
    // Reject multiline inline source (often many KB) without allocating a
    // trimmed copy of it: only a single-line unit can be a path.
    if (text.AsSpan().Trim().IndexOf('\n') >= 0) return null;
    var t = text.Trim();
    if (!LooksLikeSourcePath(t)) return null;
    var p = ResolveDocRelative(t, doc);
    return p != null && File.Exists(p) ? Path.GetFullPath(p) : null;
  }

  static bool LooksLikeSourcePath(string s)
  {
    return s.IndexOf('\n') < 0
      && (s.EndsWith(".cs", StringComparison.OrdinalIgnoreCase)
       || s.EndsWith(".py", StringComparison.OrdinalIgnoreCase));
  }

  static List<string> NonBlankLines(string text)
  {
    return text.Split('\n').Select(l => l.Trim()).Where(l => l.Length > 0).ToList();
  }

  // The expandable-branch shape test: at least two non-blank lines, every one
  // a file path. Returns the trimmed path lines, or null when the branch is
  // not a path list. Runs on every solve for every branch, so real source is
  // rejected before the per-line split: an all-paths branch must END with a
  // path line, and source text almost never does.
  static List<string> PathLines(string text)
  {
    var tail = text.AsSpan().TrimEnd();
    if (!tail.EndsWith(".cs", StringComparison.OrdinalIgnoreCase)
     && !tail.EndsWith(".py", StringComparison.OrdinalIgnoreCase)) return null;
    var lines = NonBlankLines(text);
    return lines.Count >= 2 && lines.All(LooksLikeSourcePath) ? lines : null;
  }

  static string LoadSourceIfPath(string text, GH_Document doc, GH_Path path, List<string> log)
  {
    var t = text.Trim();
    if (!LooksLikeSourcePath(t))
    {
      // An all-paths branch expanded into units before this point, so a
      // path-heavy branch surviving here means a MIXED list — likely a typo'd
      // file name among good ones — about to be compiled as source text. Warn
      // with the likely cause instead of letting the compile error mystify.
      var lines = NonBlankLines(t);
      if (lines.Count >= 2 && lines.Count(LooksLikeSourcePath) >= lines.Count - 1)
        log.Add("WARNING: branch " + path + " looks like a list of file paths with one odd line"
          + " — treating it all as source text; check for a typo'd file name.");
      return text;
    }

    var p = ResolveDocRelative(t, doc);
    if (p == null)
      throw new Exception("Source is a relative file path (" + t
        + ") but the .gh document is unsaved — save the document, or use an absolute path.");
    if (!File.Exists(p))
      throw new Exception("source file not found: " + p);
    var content = File.ReadAllText(p).TrimStart('\uFEFF');
    log.Add("source file: " + Path.GetFileName(p) + " (" + content.Length + " chars)");
    return content;
  }

  // Resolves a path against the folder of the saved .gh (absolute paths pass
  // through). Null when the path is relative but the document is unsaved —
  // the caller decides whether that throws (source files) or warns (icons).
  static string ResolveDocRelative(string p, GH_Document doc)
  {
    if (Path.IsPathRooted(p)) return p;
    var docPath = doc != null ? doc.FilePath : null;
    if (string.IsNullOrEmpty(docPath)) return null;
    return Path.Combine(Path.GetDirectoryName(docPath), p);
  }

  // Forge joins a branch's items with newlines (a branch is ONE script), so a
  // branch holding several complete multiline sources would silently compile
  // into garbage — warn and point at the graft that gives each its own branch.
  void WarnMultiSourceBranch(IList<string> items, GH_Path path, List<string> log)
  {
    if (items == null || items.Count < 2) return;
    int multi = 0;
    foreach (var it in items)
      if (it != null && it.IndexOf('\n') >= 0) multi++;
    if (multi == 0) return;
    var msg = "Source branch " + path + " holds " + items.Count + " items of which " + multi
      + " are multiline — a branch is one script; graft so each source is its own branch.";
    log.Add("WARNING: " + msg);
    Component.AddRuntimeMessage(GH_RuntimeMessageLevel.Warning, msg);
  }

  // Pairs a Source unit with its targets (a unit is a branch, or one path of
  // an expanded path list — branchIndex/sourceBranches count units). An
  // identical path wins and may carry SEVERAL targets — the source is forged
  // into each. Otherwise a single flat Target list pairs one item per unit in
  // order (or, when Source has just one unit, every item targets it; or, when
  // the flat list is ALL keywords and/or null/empty items, it broadcasts to
  // every unit). A null or empty ITEM still means forge a new component in
  // that slot; the name / nickname KEYWORDS expand to every on-canvas
  // component matching the source header (see AddTargetItem).
  // But when Target is WIRED and resolves to nothing for
  // this branch — an empty matching branch, a flat list that ran out, no
  // matching branch at all, or keywords that matched no component — the
  // branch is skipped. An UNWIRED Target walks the identity ladder instead
  // (below): header instanceGuid, else the header's own name, else forge new.
  List<Guid> ResolveTargetsForBranch(DataTree<object> target, bool wired, GH_Path path,
    int branchIndex, int sourceBranches, string text,
    Dictionary<Guid, IGH_DocumentObject> liveObjects, List<string> log, out bool skip)
  {
    skip = false;
    var result = new List<Guid>();
    if (!wired)
    {
      // The identity ladder, rungs 2-4 — a wired Target is rung 1. Walked
      // fresh on every pass, because nothing about the last press is
      // remembered. The `name` rung is what replaces the up-to-date no-op as
      // the source of idempotence: press twice and the second pass finds the
      // component the first one created, by the name its own header stamped.
      var meta = ParseHeader(text);
      if (meta == null)
      {
        result.Add(Guid.Empty); // headerless: nothing to match on — forge new
        return result;
      }
      if (meta.PinnedGuid != Guid.Empty)
      {
        log.Add("Target unwired — header instanceGuid " + meta.PinnedGuid);
        result.Add(meta.PinnedGuid);
        return result;
      }
      // No pin: match the header's own name on canvas, forging new when
      // nothing matches. Exactly the 'name+create' keyword, reached from the
      // ladder rather than from a Target item, so multiple matches update
      // together the same way.
      result.AddRange(MatchTargetsByName(text, false, true, liveObjects, log,
        "Target unwired — header name"));
      return result;
    }
    if (target == null || target.BranchCount == 0)
    {
      log.Add("Target is wired but empty — skipping (leave Target unwired to forge new components)");
      skip = true;
      return result;
    }

    IEnumerable<object> items;
    if (target.PathExists(path))
    {
      var branch = target.Branch(path);
      if (branch.Count == 0)
      {
        log.Add("Target branch " + path + " is empty — nothing to update, skipping");
        skip = true;
        return result;
      }
      items = branch;
    }
    else if (target.BranchCount == 1)
    {
      var flat = target.Branches[0];
      if (sourceBranches == 1 || AllBroadcastable(flat))
      {
        // One script, one non-matching Target branch: every item is a target
        // (the update-all-instances shape, e.g. a Metahopper object list).
        // A flat list of only keywords and/or null/empty items (usually a
        // single 'name' or a single null) likewise takes the whole list — it
        // BROADCASTS, keywords re-resolving against each branch's OWN header
        // and a null/empty item forging one NEW component per branch, so one
        // item serves every source where positional pairing would starve
        // every branch but the first.
        items = flat;
      }
      else if (branchIndex < flat.Count)
      {
        items = new[] { flat[branchIndex] };
      }
      else
      {
        log.Add("no Target item for branch " + path + " (flat Target list has "
          + flat.Count + " item(s)) — skipping");
        skip = true;
        return result;
      }
    }
    else
    {
      log.Add("no Target branch matches " + path + " — skipping");
      skip = true;
      return result;
    }

    foreach (var it in items) AddTargetItem(it, result, text, liveObjects, log);
    if (result.Count == 0)
    {
      // Only keywords resolve to nothing (a null/empty plain item still means
      // forge-new): no component on the canvas matches, so nothing to update.
      log.Add("Target keyword matched no components for branch " + path
        + " — skipping (use name+create / nickname+create to forge one instead)");
      skip = true;
    }
    return result;
  }

  // A Target item is either a match keyword — name or nickname, optionally
  // suffixed +create — or a direct reference (Guid, guid string, component
  // goo). A keyword expands to every on-canvas match for the source header;
  // a direct item adds exactly one guid. Duplicates are NOT collapsed here:
  // a component reached twice (say a keyword plus its explicit guid) hits
  // ForgeSlot's claim check, which errors the second slot — the documented
  // same-component-targeted-twice policy, keyword or not.
  void AddTargetItem(object item, List<Guid> result, string text,
    Dictionary<Guid, IGH_DocumentObject> liveObjects, List<string> log)
  {
    bool byNickname, create;
    if (TryParseTargetKeyword(item, out byNickname, out create))
      result.AddRange(MatchTargetsByName(text, byNickname, create, liveObjects, log,
        "Target keyword '" + (byNickname ? "nickname" : "name") + (create ? "+create" : "") + "'"));
    else
      result.Add(ResolveTargetGuid(item));
  }

  // The broadcast test for a flat Target list: every item is a keyword or a
  // null/empty create-new marker, so the list is position-independent and can
  // apply to every Source unit. Any direct reference (guid, goo) makes the
  // list positional again.
  static bool AllBroadcastable(IList<object> items)
  {
    if (items.Count == 0) return false;
    bool byNickname, create;
    foreach (var it in items)
      if (!TryParseTargetKeyword(it, out byNickname, out create) && !IsEmptyTargetItem(it))
        return false;
    return true;
  }

  // A null item, or a string/goo holding only whitespace — the create-new
  // marker that ResolveTargetGuid maps to Guid.Empty.
  static bool IsEmptyTargetItem(object item)
  {
    var v = ItemValue(item);
    if (v == null) return true;
    var s = v as string;
    return s != null && s.Trim().Length == 0;
  }

  // Unwraps a Target item one level: a goo yields its ScriptVariable, anything
  // else passes through. The shared first step of keyword and empty-item
  // classification.
  static object ItemValue(object item)
  {
    var goo = item as IGH_Goo;
    return goo != null ? goo.ScriptVariable() : item;
  }

  // The keyword grammar: 'name' / 'nickname', optional '+create' suffix,
  // case-insensitive. Anything else — including guid strings — is not a
  // keyword and resolves as a direct reference. Guid strings can never
  // collide with the keywords, so existing rigs are unaffected.
  static bool TryParseTargetKeyword(object item, out bool byNickname, out bool create)
  {
    byNickname = false;
    create = false;
    var s = ItemValue(item) as string;
    if (s == null) return false;
    s = s.Trim().ToLowerInvariant();
    bool plusCreate = s.EndsWith("+create");
    if (plusCreate) s = s.Substring(0, s.Length - "+create".Length);
    if (s != "name" && s != "nickname") return false;
    byNickname = s == "nickname";
    create = plusCreate;
    return true;
  }

  // Expands a name match to targets: every stock script component of the
  // source's language whose Name (or NickName) equals the source header's
  // name (or nickname), case-insensitively. The forge itself never matches.
  // No match expands to nothing — the branch skips — unless create was asked
  // for, which expands to one forge-new slot instead. Reached two ways, from a
  // Target keyword and from the unwired identity ladder; `label` is how the
  // log line says which, since the mechanism is identical.
  List<Guid> MatchTargetsByName(string text, bool byNickname, bool create,
    Dictionary<Guid, IGH_DocumentObject> liveObjects, List<string> log, string label)
  {
    string kw = (byNickname ? "nickname" : "name") + (create ? "+create" : "");
    var meta = ParseHeader(text);
    if (meta == null)
      throw new Exception("Target keyword '" + kw
        + "' needs an @component header — a headerless source has no name to match");
    // existing:null is safe — DetectPython only consults the target component
    // when meta is null, and a keyword requires a header (checked above).
    bool isPython = DetectPython(text, meta, null);
    string wantType = ComponentTypeFor(isPython);
    string wanted = byNickname ? meta.Nick : meta.Name;

    var found = new List<Guid>();
    bool selfMatched = false;
    if (liveObjects != null)
      foreach (var o in liveObjects.Values)
      {
        if (o.GetType().FullName != wantType) continue;
        string have = (byNickname ? o.NickName : o.Name) ?? "";
        if (!string.Equals(have.Trim(), wanted, StringComparison.OrdinalIgnoreCase)) continue;
        if (o.InstanceGuid == Component.InstanceGuid) { selfMatched = true; continue; }
        found.Add(o.InstanceGuid);
      }

    log.Add(label + ": " + found.Count + " component(s) "
      + (byNickname ? "nicknamed '" : "named '") + wanted + "'"
      + (selfMatched ? " (this forge itself matched and was excluded)" : ""));
    if (found.Count == 0 && create)
    {
      log.Add("no match — forging a new component");
      found.Add(Guid.Empty);
    }
    return found;
  }

  // The canvas index built once per solve answers every by-guid lookup; a
  // guid that is absent, or present but not a component, resolves to null.
  static IGH_Component FindComponent(Dictionary<Guid, IGH_DocumentObject> liveObjects, Guid guid)
  {
    IGH_DocumentObject o;
    if (liveObjects == null || !liveObjects.TryGetValue(guid, out o)) return null;
    return o as IGH_Component;
  }

  // ---------------------------------------------------------------- forge --

  Guid Forge(Dictionary<Guid, IGH_DocumentObject> liveObjects, string text, Guid targetGuid,
    GH_Path path, int t, List<string> log)
  {
    var doc = Component.OnPingDocument();
    if (doc == null) throw new Exception("component is not on a document");

    var meta = ParseHeader(text); // null when the source carries no header

    // No header-instanceGuid fallback here: ResolveTargetsForBranch's identity
    // ladder owns that rung. Reaching Forge with an empty target therefore
    // means exactly one thing — create a new component — which is what keeps
    // an explicitly empty Target item an escape hatch for a second copy even
    // when the source pins an instanceGuid.
    if (targetGuid == Component.InstanceGuid)
      throw new Exception("target is this Script Forge component itself — refusing");

    IGH_Component comp = targetGuid != Guid.Empty ? FindComponent(liveObjects, targetGuid) : null;

    bool isPython = DetectPython(text, meta, comp);
    string lang = isPython ? "Python 3" : "C#";

    if (meta == null)
    {
      // Headerless source: behave like dropping a stock script component and
      // pasting the code in — a fresh component keeps its default params
      // (x, y, out, a), an existing one keeps whatever it has. No param sync,
      // no identity, no icon; GH takes it from here.
      meta = new HeaderMeta { SyncParams = false };
      log.Add(string.Format("headerless {0} source — pushing source only, params and metadata untouched", lang));
    }
    else
    {
      log.Add(string.Format("header: {0} [{1}] ({2} in / {3} out)",
        meta.Name, lang, meta.Ins.Count, meta.Outs.Count));
      WarnDriftAndQuotes(text, meta, isPython, log);
    }

    string wantType = ComponentTypeFor(isPython);

    bool fresh = comp == null;
    var claim = RectangleF.Empty;
    if (!fresh)
    {
      string haveType = comp.GetType().FullName;
      if (haveType != CsComponentType && haveType != PyComponentType)
      {
        // Not a stock script component at all — e.g. a compiled component
        // placed from a built plugin. There is no script slot to push source
        // into, so the forge cannot touch it regardless of language.
        throw new Exception(string.Format(
          "target {0} ('{1}') is not a stock script component — it is {2}, likely a compiled "
          + "plugin component. Script Forge can only update stock C# Script / Python 3 Script "
          + "components, which carry editable source. To make this one forgeable, delete it and "
          + "re-forge with no matching target — that creates a fresh stock {3} component "
          + "(its old wires are not carried over).",
          targetGuid, comp.NickName, haveType, lang));
      }
      if (haveType != wantType)
      {
        // Forge updates a component in place, and a C# component and a Python
        // component are different classes — so a source can only refresh a
        // target of its own language. Switching languages would mean replacing
        // the component (and losing its wires), so stop with an explanatory
        // error rather than silently changing nothing.
        string haveLang = haveType == PyComponentType ? "Python 3" : "C#";
        throw new Exception(string.Format(
          "language mismatch: target {0} is an existing {1} Script, but this source is {2}. "
          + "Script Forge updates a component in place and can't convert {1} into {2}. "
          + "To switch languages, delete the {1} component first — a re-forge with no matching "
          + "target then creates a fresh {2} component (its old wires are not carried over).",
          targetGuid, haveLang, lang));
      }
      log.Add("updating existing component " + targetGuid + " (wires kept for unchanged param names)");
    }
    else
    {
      var compType = FindType(wantType);
      comp = (IGH_Component)Activator.CreateInstance(compType);
      var dobj = (GH_DocumentObject)comp;
      if (targetGuid != Guid.Empty)
      {
        dobj.NewInstanceGuid(targetGuid);
        log.Add("target guid not on canvas — creating new component pinned to it");
      }
      dobj.CreateAttributes();
      dobj.Attributes.Pivot = ClaimSpawnPivot(doc, Component.Attributes.Pivot, out claim);
      log.Add(string.Format("creating new {0} component {1}", lang, comp.InstanceGuid));
    }

    var setText = ScriptMethod(comp, "set_Text");
    if (setText == null)
      throw new Exception("target does not expose IScriptComponent.set_Text — not a script component?");

    var plan = new List<string>(log);
    var self = Component;
    var slot = Slot(path, t);
    _busyIds.Add(slot);

    bool freshPython = fresh && isPython;

    doc.ScheduleSolution(10, d =>
    {
      var rep = new List<string>(plan);
      bool mutationOk = false;
      try
      {
        if (fresh) d.AddObject((IGH_DocumentObject)comp, false);
        if (meta.SyncParams)
          rep.AddRange(SyncParams(comp, meta.Ins, meta.Outs, isPython));
        var marsh = CaptureMarshalling(comp);
        setText.Invoke(comp, new object[] { text });
        foreach (var kv in marsh) kv.Key.SetValue(comp, kv.Value);
        if (!freshPython) comp.ExpireSolution(false);
        rep.Add(fresh ? "component added, params built, source pushed"
                      : "params synced, source pushed");
        mutationOk = true;
      }
      catch (Exception ex)
      {
        var msg = "MUTATION ERROR: " + Unwrap(ex).Message;
        rep.Add(msg);
        ReportAsync(self, GH_RuntimeMessageLevel.Error, SlotName(path, t) + ": " + msg);
      }
      _claims.Remove(claim); // now in the doc (or failed) — the live scan takes over
      // This callback runs BEFORE the target compiles, so its lines (param
      // sync, lost wires, the push itself) still belong to the story Log
      // tells; replace the synchronous report with this fuller one. It reaches
      // Log on the forge's next solve, whenever that is — never staler than
      // what the unit loop stored a moment ago, only more complete.
      _reports[slot] = rep;

      GH_Document.GH_ScheduleDelegate stamp = d2 =>
      {
        var notes = new List<string>();
        try { StampAll(comp, meta, d2, notes); }
        catch (Exception ex) { notes.Add("STAMP ERROR: " + Unwrap(ex).Message); }
        _busyIds.Remove(slot);
        // Stamping runs after the target has COMPILED, which is after this
        // component's own solve ended — so nothing here can reach Log, and the
        // forge no longer expires itself to fetch it back. A clean pass says
        // nothing at all; a failure raises the forge's own bubble.
        foreach (var n in notes)
        {
          if (IsOwnError(n))
            ReportAsync(self, GH_RuntimeMessageLevel.Error, SlotName(path, t) + ": " + n);
          else if (n.StartsWith("icon warning:"))
            ReportAsync(self, GH_RuntimeMessageLevel.Warning, SlotName(path, t) + ": " + n);
        }
      };

      if (freshPython && mutationOk)
        // Give the new component's script engine a longer settle window before
        // the ExpireSolution that fires its first-ever solve — see the
        // FreshPythonSettleMs comment above.
        d.ScheduleSolution(FreshPythonSettleMs, d1 =>
        {
          comp.ExpireSolution(false);
          d1.ScheduleSolution(10, stamp);
        });
      else
        d.ScheduleSolution(10, stamp);
    });

    log.Add("push scheduled — params and source apply on the next solution, stamping on the one after");
    return comp.InstanceGuid;
  }

  // ----------------------------------------------------------- file watch --
  //
  // Auto re-forge on external file edits. When a Source is a .cs/.py FILE PATH
  // (see LoadSourceIfPath), a live edit to the file should re-forge with no
  // Read File rig — but a script component only re-solves when an input value
  // changes or something expires it, and a static path string never changes.
  // So while Run is true, SyncWatchers attaches a FileSystemWatcher and, on a
  // watched file changing, RECORDS THE PATH and expires this component; the
  // ordinary solve then re-reads that file and pushes just the unit that names
  // it. With change detection gone the recorded path is the whole trigger,
  // which is what makes the debounce load-bearing rather than an optimisation:
  // one save fires several events, and each survivor would be its own push.
  //
  // State here is keyed per instance guid — coarser than the per-Slot
  // (instance|path|target) statics — because a watcher expires the WHOLE
  // component: expiry re-reads every branch and re-runs the per-slot hash, so
  // the branch and target axes are irrelevant and the watched set is a union
  // across all of an instance's units. Only instance identity matters, to keep
  // forge components sharing one compiled assembly from cross-wiring watchers.
  // Directory watchers (not
  // per-file) are used because many editors save via a temp file + rename,
  // which raises no Changed on the original handle; events are filtered down to
  // the exact absolute paths we care about, and debounced because one save
  // fires several. FileSystemWatcher raises on a thread-pool thread, so the
  // expire is marshalled to the UI thread (Rhino.RhinoApp.InvokeOnUiThread).

  class WatchSet
  {
    public HashSet<string> Paths;                // absolute file paths watched
    public HashSet<string> Pending;              // paths changed since the last solve
    public List<FileSystemWatcher> Watchers;     // one per distinct directory
    public System.Threading.Timer Debounce;      // fires once the edits settle
    public IGH_Component Comp;                    // survives Script_Instance recompiles
  }

  static readonly Dictionary<Guid, WatchSet> _watchers = new Dictionary<Guid, WatchSet>();
  static readonly object _watchLock = new object();
  const int WatchDebounceMs = 250;

  // Reconcile this instance's live watchers with the file-path Sources of the
  // current pass. Cheap and idempotent: an unchanged path set leaves the live
  // watchers untouched (no handle churn); Run false or no path sources tear
  // them all down.
  void SyncWatchers(bool run, List<SourceUnit> units, GH_Document doc)
  {
    // Every unit already carries the absolute file it would read (WatchPathOf,
    // resolved once in RunScript), so the set watched and the set a push can be
    // triggered from are the same set by construction.
    var want = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
    if (run)
      foreach (var u in units)
        if (u.WatchPath != null) want.Add(u.WatchPath);

    var id = Component.InstanceGuid;
    lock (_watchLock)
    {
      WatchSet cur;
      _watchers.TryGetValue(id, out cur);
      // Unchanged non-empty set: keep the live handles, no churn.
      if (cur != null && want.Count > 0 && cur.Paths.SetEquals(want)) return;
      if (cur != null) Reclaim(id);
      if (want.Count == 0) return; // Run off, or no path sources — stay disarmed.

      var set = new WatchSet
      {
        Paths = want,
        Pending = new HashSet<string>(StringComparer.OrdinalIgnoreCase),
        Watchers = new List<FileSystemWatcher>(),
        Comp = Component,
      };
      set.Debounce = new System.Threading.Timer(_ => OnWatchedChange(id), null,
        System.Threading.Timeout.Infinite, System.Threading.Timeout.Infinite);

      foreach (var dir in want.Select(Path.GetDirectoryName).Distinct(StringComparer.OrdinalIgnoreCase))
      {
        FileSystemWatcher w = null;
        try
        {
          w = new FileSystemWatcher(dir)
          {
            NotifyFilter = NotifyFilters.LastWrite | NotifyFilters.FileName | NotifyFilters.Size,
            IncludeSubdirectories = false,
          };
          FileSystemEventHandler onChange = (s, e) => { if (want.Contains(e.FullPath)) Bump(id, e.FullPath); };
          w.Changed += onChange;
          w.Created += onChange;
          w.Renamed += (s, e) => onChange(s, e); // RenamedEventArgs : FileSystemEventArgs
          w.EnableRaisingEvents = true;
          set.Watchers.Add(w);
        }
        catch
        {
          // A folder we can't watch (permissions, gone) just won't auto-forge;
          // the manual Run press still works. Skip it, keep the rest.
          if (w != null) w.Dispose();
        }
      }
      EnsureSubscribed(doc);
      _watchers[id] = set;
    }
  }

  // Record which file changed and restart the debounce timer, coalescing the
  // burst of events one save produces into a single expire once
  // WatchDebounceMs of quiet has passed. The recorded path is the trigger the
  // next solve reads — an expire on its own would arrive as an ordinary solve
  // with Run merely held true, which pushes nothing.
  static void Bump(Guid id, string fullPath)
  {
    lock (_watchLock)
    {
      WatchSet set;
      if (!_watchers.TryGetValue(id, out set)) return;
      set.Pending.Add(fullPath);
      set.Debounce.Change(WatchDebounceMs, System.Threading.Timeout.Infinite);
    }
  }

  // Take and clear the paths recorded since the last solve. Drained
  // unconditionally at the top of every solve, so an event that arrived while
  // Run was being switched off does not sit waiting to fire later.
  static HashSet<string> DrainPending(Guid id)
  {
    lock (_watchLock)
    {
      WatchSet set;
      if (!_watchers.TryGetValue(id, out set) || set.Pending.Count == 0)
        return new HashSet<string>(StringComparer.OrdinalIgnoreCase);
      var drained = set.Pending;
      set.Pending = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
      return drained;
    }
  }

  // Debounce elapsed (on a thread-pool thread): marshal to the UI thread and
  // expire the component so GH re-solves it. Deletion and document-close are
  // reclaimed promptly by OnObjectsDeleted / OnDocumentRemoved; the liveness
  // check here is a backstop for any path that fires neither — a removed
  // component never solves again, so SyncWatchers alone can't tear it down.
  static void OnWatchedChange(Guid id)
  {
    IGH_Component comp;
    lock (_watchLock)
    {
      WatchSet set;
      if (!_watchers.TryGetValue(id, out set)) return;
      comp = set.Comp;
    }
    if (comp == null) return;
    Rhino.RhinoApp.InvokeOnUiThread((Action)(() =>
    {
      var doc = comp.OnPingDocument();
      bool live = doc != null && doc.Objects.Any(o => o.InstanceGuid == id);
      if (!live)
      {
        lock (_watchLock)
        {
          WatchSet gone;
          if (_watchers.TryGetValue(id, out gone) && gone.Comp == comp)
            Reclaim(id);
        }
        return;
      }
      comp.ExpireSolution(true);
    }));
  }

  static void DisposeWatchSet(WatchSet set)
  {
    if (set == null) return;
    set.Debounce.Dispose();
    foreach (var w in set.Watchers)
      try { w.EnableRaisingEvents = false; w.Dispose(); } catch { }
  }

  // Dispose an instance's watch set and drop it from the table. The single
  // reclaim primitive behind every teardown path. Caller holds _watchLock.
  static void Reclaim(Guid id)
  {
    WatchSet set;
    if (_watchers.TryGetValue(id, out set))
    { DisposeWatchSet(set); _watchers.Remove(id); }
  }

  // Deterministic teardown. A stock script component can't override
  // RemovedFromDocument (GH_ScriptInstance exposes no such hook), so we
  // subscribe to document events at runtime instead: DocumentRemoved reclaims a
  // whole document's watchers when it closes; ObjectsDeleted reclaims a single
  // instance's the moment it is deleted, rather than waiting for the next file
  // event. Subscribed once per compiled-assembly lifetime — recompiling Forge
  // itself resets these statics and orphans the prior subscription (a harmless
  // no-op on documents it no longer tracks, reclaimed by GC), which a script
  // component has no unload hook to avoid. Called under _watchLock as watchers
  // are armed, so a live instance's document is always subscribed.
  static bool _subscribed;
  static readonly HashSet<GH_Document> _subbedDocs = new HashSet<GH_Document>();

  void EnsureSubscribed(GH_Document doc)
  {
    if (!_subscribed)
    {
      Grasshopper.Instances.DocumentServer.DocumentRemoved += OnDocumentRemoved;
      _subscribed = true;
    }
    if (doc != null && _subbedDocs.Add(doc))
      doc.ObjectsDeleted += OnObjectsDeleted;
  }

  // A document closed: dispose the watchers of every instance it held, and drop
  // that document's ObjectsDeleted hook. Reclaiming already-orphaned sets from
  // other documents is the OnWatchedChange backstop's job, not this handler's.
  static void OnDocumentRemoved(GH_DocumentServer server, GH_Document doc)
  {
    if (doc == null) return;
    lock (_watchLock)
    {
      doc.ObjectsDeleted -= OnObjectsDeleted;
      _subbedDocs.Remove(doc);
      foreach (var obj in doc.Objects)
        Reclaim(obj.InstanceGuid);
    }
  }

  // Objects were deleted from a document: dispose watchers for any that are ours.
  static void OnObjectsDeleted(object sender, GH_DocObjectEventArgs e)
  {
    lock (_watchLock)
      foreach (var obj in e.Objects)
        Reclaim(obj.InstanceGuid);
  }

  // ------------------------------------------------------------- placement --

  // New components land in a column one bay right of the forge, in the first
  // slot no live canvas object occupies. Scanning the document instead of
  // counting spawns keeps placement anchored to the forge: deleting or moving
  // a component frees its slot, so nothing drifts down the canvas over a long
  // session. _claims covers the window between choosing a pivot and the
  // scheduled solution adding the component — sibling branches forged in the
  // same pass are not in the document yet, so the scan alone cannot see them.
  static PointF ClaimSpawnPivot(GH_Document doc, PointF forge, out RectangleF claim)
  {
    const float BayX = 320f, StepY = 140f, SlotW = 240f, SlotH = 130f;
    float x = forge.X + BayX;
    float y = forge.Y;
    for (int k = 0; k < 500; k++)
    {
      y = forge.Y + k * StepY;
      var rect = new RectangleF(x - SlotW / 2f, y - SlotH / 2f, SlotW, SlotH);
      if (SlotOccupied(doc, rect)) continue;
      claim = rect;
      _claims.Add(rect);
      return new PointF(x, y);
    }
    claim = RectangleF.Empty; // canvas column is packed solid — overlap rather than fly off
    return new PointF(x, y);
  }

  static bool SlotOccupied(GH_Document doc, RectangleF rect)
  {
    foreach (var c in _claims)
      if (c.IntersectsWith(rect)) return true;
    foreach (var o in doc.Objects)
    {
      var att = o.Attributes;
      if (att != null && att.Bounds.IntersectsWith(rect)) return true;
    }
    return false;
  }

  // ------------------------------------------------------------ param sync --
  // Native replacement for what a param-management plugin would do: keep the
  // existing IGH_Param OBJECTS for unchanged names (their Sources/Recipients
  // lists are what the wires live on), recycle leftover params positionally
  // for renamed ones (so index-style renames keep their wires too), create
  // what is still missing, drop the rest, and re-register everything in
  // header order. The stdout param — identified by TYPE (the one output that is
  // not a ScriptVariableParam), never by name or index — is kept at output
  // index 0 when present, and its absence (hidden via the right-click
  // standard-output toggle) is equally fine.

  List<string> SyncParams(IGH_Component comp, List<HeaderParam> ins, List<HeaderParam> outs, bool isPython)
  {
    var notes = new List<string>();
    var vpc = comp as IGH_VariableParameterComponent;
    if (vpc == null) throw new Exception("component does not support variable parameters");
    var server = comp.Params;

    // On Python components an output type hint is an ACTIVE converter that is
    // applied to the assigned PyObject wholesale — a hinted list output fails
    // with a type-conversion error. Stock Python outputs are unhinted, so we
    // never hint Python outputs; an output's header `type` stays documentation.
    // (C# outputs are write-only object sinks, so their hints are harmless.)

    // inputs
    var oldIns = server.Input.ToList();
    var desiredIns = MatchParams(ins, oldIns, GH_ParameterSide.Input, vpc, notes, "input", true);
    foreach (var removed in oldIns)
    {
      if (removed.SourceCount > 0)
        notes.Add(string.Format("lost wire: input {0} ({1} source(s))", removed.NickName, removed.SourceCount));
      server.UnregisterInputParameter(removed, true);
    }
    foreach (var p in desiredIns) server.UnregisterInputParameter(p, false);
    foreach (var p in desiredIns) server.RegisterInputParam(p);

    // outputs
    var oldOuts = server.Output.ToList();
    var stdout = oldOuts.FirstOrDefault(p => p.GetType().Name != "ScriptVariableParam");
    if (stdout != null) oldOuts.Remove(stdout);
    var desiredOuts = MatchParams(outs, oldOuts, GH_ParameterSide.Output, vpc, notes, "output", !isPython);
    if (stdout != null) desiredOuts.Insert(0, stdout);
    foreach (var removed in oldOuts)
    {
      if (removed.Recipients.Count > 0)
        notes.Add(string.Format("lost wire: output {0} ({1} recipient(s))", removed.NickName, removed.Recipients.Count));
      server.UnregisterOutputParameter(removed, true);
    }
    foreach (var p in desiredOuts) server.UnregisterOutputParameter(p, false);
    foreach (var p in desiredOuts) server.RegisterOutputParam(p);

    server.OnParametersChanged();
    vpc.VariableParameterMaintenance();
    return notes;
  }

  // Pairs header defs with existing params: by name first, then leftover
  // params are recycled positionally (a rename-in-place keeps its wires),
  // then fresh params are created. Matched params are REMOVED from oldParams;
  // whatever remains there is deleted by the caller.
  List<IGH_Param> MatchParams(List<HeaderParam> defs, List<IGH_Param> oldParams,
    GH_ParameterSide side, IGH_VariableParameterComponent vpc, List<string> notes, string label, bool applyHints)
  {
    var matched = new IGH_Param[defs.Count];
    for (int i = 0; i < defs.Count; i++)
    {
      // Key on NickName: it is the identifier, and the one field the user
      // cannot repoint (Name may carry the header's pretty name, or one the
      // user typed into the param's right-click "Name (for humans)" box).
      var p = oldParams.FirstOrDefault(x => x.NickName == defs[i].VariableName);
      if (p != null) { matched[i] = p; oldParams.Remove(p); }
    }
    for (int i = 0; i < defs.Count; i++)
    {
      if (matched[i] != null || oldParams.Count == 0) continue;
      var p = oldParams[0];
      oldParams.RemoveAt(0);
      int wires = side == GH_ParameterSide.Input ? p.SourceCount : p.Recipients.Count;
      if (wires > 0)
        notes.Add(string.Format("renamed {0} {1} -> {2} ({3} wire(s) kept)", label, p.NickName, defs[i].VariableName, wires));
      matched[i] = p;
    }
    var result = new List<IGH_Param>();
    for (int i = 0; i < defs.Count; i++)
    {
      var p = matched[i] ?? vpc.CreateParameter(side, i);
      ConfigureParam(p, defs[i], notes, applyHints, side == GH_ParameterSide.Output);
      result.Add(p);
    }
    return result;
  }

  void ConfigureParam(IGH_Param p, HeaderParam def, List<string> notes, bool applyHint, bool isOutput)
  {
    // NickName is the identifier: on a ScriptVariableParam it IS VariableName,
    // it is what GH draws on the param, and Rhino validates it as a legal C#
    // identifier. Name is PrettyName — an optional alias Rhino reserves for a
    // human label and does not validate — so the header's name goes there. With
    // no separate variableName the two are the same string, as before. The param
    // still draws the variable name; the tooltip reads `Name (VariableName)`.
    p.NickName = def.VariableName;
    p.Name = def.Name;
    // Both new header keys are INPUT-side ideas — Grasshopper only ever collects
    // into an input — so an output lands exactly where every param used to:
    // Optional, no default. WarnDriftAndQuotes warns at a header that tries
    // either on an output, and this is what makes the warning true rather than
    // merely disapproving. On an input, `optional: false` makes an unwired param
    // stop the solve outright, which is the point of asking for it.
    p.Optional = isOutput || def.Optional;
    // Access arrives canonical from the header parser; compared ignoring case
    // anyway, because getting this wrong silently builds an `item` param out of
    // a header that validated clean.
    p.Access = string.Equals(def.Access, "list", StringComparison.OrdinalIgnoreCase)
                 ? GH_ParamAccess.list
             : string.Equals(def.Access, "tree", StringComparison.OrdinalIgnoreCase)
                 ? GH_ParamAccess.tree
             : GH_ParamAccess.item;
    // applyHint=false means Python output: force No Type Hint, clearing any
    // hint already selected on the param — see the Python-outputs note in SyncParams.
    var note = ApplyHint(p, applyHint ? def.Hint : "object");
    if (note != null) notes.Add(note);
    if (!isOutput) ApplyDefault(p, def, notes);
  }

  // Seeds a param's PersistentData from a declared `default` — the internalised
  // value GH hands the script when nothing is wired in.
  //
  // **It only ever seeds an EMPTY slot.** Persistent data is user-visible and
  // user-editable (right-click ▸ Set Boolean / Internalise data), and a forge
  // re-runs on every source edit, so re-stamping would quietly undo whatever the
  // user typed — several times a minute during iteration. The cost of that
  // choice is that a header can seed a default but never *reset* one; clearing
  // is the user's job, via right-click ▸ Destroy persistent data. Revisit if it
  // bites, but not by stamping unconditionally.
  void ApplyDefault(IGH_Param p, HeaderParam def, List<string> notes)
  {
    if (def.Default == null || DefaultProblem(def, false) != null) return;  // already warned

    // PersistentData lives on GH_PersistentParam<T>, not on IGH_Param, so it is
    // out of reach without reflection — the same reach the kit's Flag() helper
    // takes for PersistentDataCount, and for the same reason: it keeps this
    // agnostic about which param class the script component actually built.
    var t = p.GetType();
    var count = t.GetProperty("PersistentDataCount");
    // The user's own value wins — and because that is the case on every re-forge
    // after the first, bail here rather than below: resolving the setter walks
    // the param type's whole method table, and this is the common path.
    if (count != null && (int)count.GetValue(p) > 0) return;

    // SetPersistentData APPENDS rather than replaces, which is why seeding only
    // an empty slot is the rule and not merely a courtesy: a second pass over a
    // filled slot would leave two items where the header declared one.
    var goo = Defaultable[def.Hint].Goo(def.Default);
    var setter = t.GetMethods().FirstOrDefault(m => m.Name == "SetPersistentData"
      && m.GetParameters().Length == 1
      && m.GetParameters()[0].ParameterType.IsInstanceOfType(goo));
    if (count == null || setter == null)
    {
      notes.Add("DEFAULT WARNING: " + def.VariableName + " has no persistent data slot — default not applied");
      return;
    }
    setter.Invoke(p, new object[] { goo });
    notes.Add("default " + def.VariableName + " = " + FormatDefault(def.Default));
  }

  // Header hint -> converter names to try, in order, against the param's own
  // hint set (which is language-specific: e.g. the double converter is named
  // float on Python, hence the bidirectional double/float and string/str
  // pairs). Hints not in this table are tried verbatim.
  static readonly Dictionary<string, string[]> HintCandidates =
    new Dictionary<string, string[]>(StringComparer.OrdinalIgnoreCase)
  {
    { "point", new[] { "Point3d" } }, { "vector", new[] { "Vector3d" } },
    { "plane", new[] { "Plane" } }, { "curve", new[] { "Curve" } },
    { "brep", new[] { "Brep" } }, { "mesh", new[] { "Mesh" } },
    { "color", new[] { "Color" } },
    { "object", new[] { "No Type Hint" } }, { "", new[] { "No Type Hint" } },
    { "none", new[] { "No Type Hint" } },
    { "double", new[] { "double", "float" } }, { "float", new[] { "float", "double" } },
    { "string", new[] { "string", "str" } }, { "str", new[] { "str", "string" } },
  };

  // Selects the type-hint Converter on a ScriptVariableParam by name, matching
  // case-insensitively via HintCandidates against the param's own hint set.
  string ApplyHint(IGH_Param p, string wanted)
  {
    var thp = p.GetType().GetProperty("TypeHints");
    if (thp == null) return "hint skipped: " + p.NickName + " has no hint set";
    var hints = thp.GetValue(p);
    var names = new List<string>();
    foreach (var h in (IEnumerable)hints)
      names.Add((string)h.GetType().GetProperty("TypeName").GetValue(h));

    string[] candidates;
    if (!HintCandidates.TryGetValue(wanted, out candidates)) candidates = new[] { wanted };

    string resolved = null;
    foreach (var c in candidates)
    {
      resolved = names.FirstOrDefault(n => string.Equals(n, c, StringComparison.OrdinalIgnoreCase));
      if (resolved != null) break;
    }
    string warn = null;
    if (resolved == null)
    {
      warn = string.Format("HINT WARNING: {0} — unknown hint {1}, using No Type Hint", p.NickName, wanted);
      resolved = "No Type Hint";
    }
    hints.GetType().GetMethod("Select", new[] { typeof(string) })
      .Invoke(hints, new object[] { resolved });
    return warn;
  }

  // -------------------------------------------------------------- stamping --

  void StampAll(IGH_Component comp, HeaderMeta meta, GH_Document doc, List<string> rep)
  {
    var dobj = (GH_DocumentObject)comp;
    if (meta.Name != null)
    {
      // Identity must go through the script interface: script components
      // regenerate the GH-side Description from their script-side slot on
      // every solve, so writing only the GH property does not stick (the
      // hover tooltip would lose its description on the next recompute).
      Action<string, string> setViaScript = (member, value) =>
      {
        var m = ScriptMethod(comp, member);
        if (m != null) m.Invoke(comp, new object[] { value });
      };
      setViaScript("set_Name", meta.Name);
      setViaScript("set_NickName", meta.Nick);
      setViaScript("set_Description", meta.Desc);
      dobj.Name = meta.Name;
      dobj.NickName = meta.Nick;
      dobj.Description = meta.Desc;
      // ...and the durable slot, which is a third property again. The writes
      // above hold only for this session; what the .gh archives is `Tooltip`
      // (declared on BaseScriptComponent<,>, lowercase t — NOT the parameter's
      // ToolTip below), and on load it is restored and overwrites Description
      // outright, so a stale Tooltip silently clobbers a freshly stamped one.
      // Both writes are required and neither substitutes for the other.
      // Measurements: the kit's docs/write-scripts/identity-properties.md.
      SetDurableString(comp, "Tooltip", meta.Desc);
    }

    var missing = new List<string>();
    int din = StampDescriptions(comp.Params.Input, meta.Ins, "input", missing);
    int dout = StampDescriptions(comp.Params.Output, meta.Outs, "output", missing);
    if (meta.Name != null)
      rep.Add(string.Format("stamped identity + descriptions ({0} in / {1} out)", din, dout));
    rep.AddRange(missing);

    // The icon is strictly best-effort: any failure becomes a warning and the
    // rest of the stamping (and the report) always completes.
    try
    {
      var bmp = ResolveIconBitmap(meta.Icon, doc, rep);
      if (bmp != null)
      {
        dobj.SetIconOverride(bmp);
        var idm = dobj.GetType().GetProperty("IconDisplayMode");
        if (idm != null && idm.CanWrite) idm.SetValue(dobj, Enum.Parse(idm.PropertyType, "icon"));
        try
        {
          var destroy = dobj.GetType().GetMethod("DestroyIconCache",
            BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.FlattenHierarchy);
          if (destroy != null) destroy.Invoke(dobj, null);
        }
        catch { /* CSharpComponent NREs in DestroyIconCache; the override still applies */ }
        rep.Add("icon stamped");
      }
    }
    catch (Exception ex)
    {
      rep.Add("icon warning: " + Unwrap(ex).Message + " — icon skipped");
    }

    // The target's own runtime messages are deliberately NOT harvested here
    // any more. They are settled state — read after the target compiled, which
    // is past the point anything can reach Log — and they belong to the forged
    // script rather than to the forge, which is why they always showed on the
    // target's own bubble too. Log now stops at the push.

    comp.Attributes.ExpireLayout();
    try { Grasshopper.Instances.ActiveCanvas?.Refresh(); } catch { }
  }

  // One side of the header/param pairing: stamp the description onto params whose
  // names match a header def, report defs with no matching param. Matching is
  // by exact NickName — the identifier, and the same rule MatchParams pairs by.
  // NOT Name: ConfigureParam has already put the header's pretty name there.
  //
  // Write ToolTip, not Description, wherever the param has one: on a
  // ScriptVariableParam ToolTip backs _descriptionOverride, which is archived into
  // the .gh AND feeds Description for the current session, so the one write does
  // both — whereas Description on its own is overwritten by UpdateFromConverter()
  // with the converter's generic text the next time the document is opened.
  //
  // The Description fallback guards any param class that has no ToolTip member —
  // a plain Param_String, say — which would otherwise be stamped with nothing at
  // all. It is NOT what handles the built-in `out` print param: no header can
  // declare `out`, so that param is skipped at the name-match guard below and
  // keeps its stock description. Verified 2026-08-05 on a forged component.
  // Measurements: the kit's docs/write-scripts/identity-properties.md.
  static int StampDescriptions(List<IGH_Param> ps, List<HeaderParam> defs, string label, List<string> missing)
  {
    int stamped = 0;
    foreach (var p in ps)
    {
      var d = defs.FirstOrDefault(x => x.VariableName == p.NickName);
      if (d == null || d.Desc.Length == 0) continue;
      if (!SetDurableString(p, "ToolTip", d.Desc)) p.Description = d.Desc;
      stamped++;
    }
    foreach (var d in defs)
      if (ps.All(x => x.NickName != d.VariableName)) missing.Add("MISSING " + label + " param: " + d.VariableName);
    return stamped;
  }

  // icon: accepts an .svg path, a .png path, or an embedded png as
  // base64:<payload> / data:image/png;base64,<payload>. Base64 may wrap over
  // header continuation lines — the decoder ignores the joining whitespace.
  Bitmap ResolveIconBitmap(string iconField, GH_Document doc, List<string> rep)
  {
    if (string.IsNullOrWhiteSpace(iconField)) return null;
    var f = iconField.Trim();

    string b64 = null;
    if (f.StartsWith("base64:", StringComparison.OrdinalIgnoreCase)) b64 = f.Substring(7);
    else if (f.StartsWith("data:", StringComparison.OrdinalIgnoreCase))
    {
      var i = f.IndexOf("base64,", StringComparison.OrdinalIgnoreCase);
      if (i < 0) { rep.Add("icon warning: data: uri without base64 payload — skipped"); return null; }
      b64 = f.Substring(i + 7);
    }
    if (b64 != null)
    {
      try
      {
        var bytes = Convert.FromBase64String(b64);
        using (var ms = new MemoryStream(bytes))
        using (var tmp = new Bitmap(ms))
          return new Bitmap(tmp); // clone so the bitmap outlives the stream
      }
      catch (Exception ex)
      {
        rep.Add("icon warning: embedded base64 icon failed to decode (" + ex.Message + ") — skipped");
        return null;
      }
    }

    var png = ResolveIconPng(f, doc, rep);
    if (png == null) return null;
    try
    {
      using (var tmp = new Bitmap(png)) return new Bitmap(tmp); // detach so the file is not locked
    }
    catch (Exception ex)
    {
      rep.Add("icon warning: could not read " + Path.GetFileName(png) + " (" + ex.Message + ") — skipped");
      return null;
    }
  }

  string ResolveIconPng(string iconField, GH_Document doc, List<string> rep)
  {
    var p = ResolveDocRelative(iconField, doc);
    if (p == null)
    {
      rep.Add("icon warning: relative icon path but the .gh document is unsaved — skipped");
      return null;
    }

    if (p.EndsWith(".png", StringComparison.OrdinalIgnoreCase))
    {
      if (File.Exists(p)) return p;
      rep.Add("icon warning: png not found: " + p);
      return null;
    }
    if (!p.EndsWith(".svg", StringComparison.OrdinalIgnoreCase))
    {
      rep.Add("icon warning: unsupported icon type: " + p);
      return null;
    }

    var png = Path.ChangeExtension(p, ".png");
    bool svgExists = File.Exists(p);
    if (File.Exists(png) && (!svgExists || File.GetLastWriteTimeUtc(png) >= File.GetLastWriteTimeUtc(p)))
      return png;
    if (!svgExists)
    {
      rep.Add("icon warning: svg not found: " + p);
      return null;
    }

    if (!File.Exists("/usr/bin/sips"))
    {
      rep.Add("icon warning: SVG rasterizing needs macOS sips — put a 24x24 PNG next to the SVG, or use a png path or base64 icon instead");
      return null;
    }

    try
    {
      // sips is built into macOS and renders the SVG at target size (24 px canvas slot).
      var psi = new System.Diagnostics.ProcessStartInfo
      {
        FileName = "/usr/bin/sips",
        Arguments = string.Format("-s format png -z 24 24 \"{0}\" --out \"{1}\"", p, png),
        UseShellExecute = false,
        RedirectStandardOutput = true,
        RedirectStandardError = true,
        CreateNoWindow = true,
      };
      using (var proc = System.Diagnostics.Process.Start(psi))
      {
        // runs on the solver/UI thread, so keep the ceiling low — a 24 px
        // render takes well under a second when sips is healthy
        if (!proc.WaitForExit(5000))
        {
          try { proc.Kill(); } catch { }
          rep.Add("icon warning: sips timed out rasterizing " + p);
          return null;
        }
      }
      if (File.Exists(png)) return png;
      rep.Add("icon warning: sips produced no png for " + p);
    }
    catch (Exception ex)
    {
      rep.Add("icon warning: rasterize failed: " + ex.Message);
    }
    return null;
  }

  // --------------------------------------------------------------- helpers --

  // set_Text can clear a Python component's Marshal Inputs/Outputs/Guids
  // toggles as a side effect of rebuilding script state (constructor and
  // stock-UI defaults are all TRUE) — without MarshOutputs a list assigned to
  // an output stays wrapped as ONE PyObject goo instead of becoming a GH list.
  // So every source push captures the toggles first and restores them right
  // after: a fresh component keeps the stock defaults, an update keeps
  // whatever the user has chosen. C# components lack these properties —
  // capture is empty.
  //
  // Measured 2026-08-26 on Rhino 8.34.26223.11002: set_Text only clears them
  // while the component is DETACHED. The callback above adds a fresh component
  // to the document before pushing, so on 8.34 this capture/restore is a no-op
  // — verified end to end, a forged Python component came back TRUE/TRUE/TRUE
  // with its list output unwrapped. KEEP IT ANYWAY: it costs nothing, and it
  // is what makes that ordering safe to change. See
  // docs/write-scripts/python3-marshalling.md for the full split.
  static List<KeyValuePair<PropertyInfo, object>> CaptureMarshalling(IGH_Component comp)
  {
    var saved = new List<KeyValuePair<PropertyInfo, object>>();
    foreach (var name in new[] { "MarshInputs", "MarshOutputs", "MarshGuids" })
    {
      var p = comp.GetType().GetProperty(name);
      if (p != null && p.CanRead && p.CanWrite)
        saved.Add(new KeyValuePair<PropertyInfo, object>(p, p.GetValue(comp)));
    }
    return saved;
  }

  // Resolves a member of RhinoCodePlatform.GH.IScriptComponent to the target's
  // implementing method; null when the interface or member is absent.
  static MethodInfo ScriptMethod(IGH_Component comp, string name)
  {
    var iface = comp.GetType().GetInterfaces()
      .FirstOrDefault(i => i.FullName == "RhinoCodePlatform.GH.IScriptComponent");
    if (iface == null) return null;
    var map = comp.GetType().GetInterfaceMap(iface);
    int idx = Array.FindIndex(map.InterfaceMethods, m => m.Name == name);
    return idx < 0 ? null : map.TargetMethods[idx];
  }

  // Property-side sibling of ScriptMethod: writes a string property by name on a
  // type this file cannot reference at compile time. Both durable tooltip slots
  // are reached through it — the component's `Tooltip` and a param's `ToolTip`.
  // Returns false when the target has no such writable string property, so the
  // caller can fall back (a plain param has no ToolTip; only Description).
  // DeclaredOnly + an explicit BaseType walk rather than one flattened lookup:
  // that covers a slot declared non-publicly on a base — GetProperty does not
  // return those — and cannot throw AmbiguousMatchException if a derived type
  // ever shadows the name.
  static bool SetDurableString(object target, string name, string value)
  {
    for (var t = target.GetType(); t != null; t = t.BaseType)
    {
      var p = t.GetProperty(name, BindingFlags.Public | BindingFlags.NonPublic
        | BindingFlags.Instance | BindingFlags.DeclaredOnly);
      if (p == null || !p.CanWrite || p.PropertyType != typeof(string)) continue;
      p.SetValue(target, value);
      return true;
    }
    return false;
  }

  // The AppDomain sweep enumerates every type in every loaded assembly, so
  // cache hits (only two type names are ever asked for).
  static readonly Dictionary<string, Type> _typeCache = new Dictionary<string, Type>();

  static Type FindType(string fullName)
  {
    Type t;
    if (_typeCache.TryGetValue(fullName, out t)) return t;
    t = AppDomain.CurrentDomain.GetAssemblies()
      .SelectMany(a => { try { return a.GetTypes(); } catch { return new Type[0]; } })
      .FirstOrDefault(x => x.FullName == fullName);
    if (t == null) throw new Exception("type not loaded: " + fullName);
    _typeCache[fullName] = t;
    return t;
  }

  static Guid ResolveTargetGuid(object o)
  {
    if (o == null) return Guid.Empty;
    if (o is Guid) return (Guid)o;
    if (o is GH_Guid) return ((GH_Guid)o).Value;
    var s = o as string;
    if (s != null)
    {
      s = s.Trim();
      if (s.Length == 0) return Guid.Empty;
      Guid g;
      if (Guid.TryParse(s, out g)) return g;
      throw new Exception("Target string is not a guid: " + s);
    }
    var ido = o as IGH_DocumentObject;
    if (ido != null) return ido.InstanceGuid;
    var goo = o as IGH_Goo;
    if (goo != null)
    {
      var v = goo.ScriptVariable();
      if (!(v is IGH_Goo) && v != o) return ResolveTargetGuid(v);
      var str = goo.ToString();
      Guid g2;
      if (Guid.TryParse(str, out g2)) return g2;
    }
    throw new Exception("Target must be a Guid, guid string, or component reference — got " + o.GetType().Name);
  }

  static Exception Unwrap(Exception ex)
  {
    var tie = ex as TargetInvocationException;
    return tie != null && tie.InnerException != null ? tie.InnerException : ex;
  }

  // ------------------------------------------------------------ validation --
  // Warn (never block) on the two silent failure modes: header/signature
  // drift (the canvas hints win over the signature, so a mismatch computes
  // wrong without erroring) and double quotes in descriptions (they break the
  // ScriptEditor plugin builder if the component is ever published).

  static readonly Regex ReIdentifier = new Regex(@"^[A-Za-z_][A-Za-z0-9_]*$");

  // C#'s reserved keywords. Contextual keywords (var, value, async, record, …)
  // are legal identifiers and are deliberately absent. SYNC: gh_meta.py's
  // _CS_KEYWORDS.
  static readonly HashSet<string> CsKeywords = new HashSet<string>((
    "abstract as base bool break byte case catch char checked class const continue "
    + "decimal default delegate do double else enum event explicit extern false finally "
    + "fixed float for foreach goto if implicit in int interface internal is lock long "
    + "namespace new null object operator out override params private protected public "
    + "readonly ref return sbyte sealed short sizeof stackalloc static string struct "
    + "switch this throw true try typeof uint ulong unchecked unsafe ushort using "
    + "virtual void volatile while").Split(' '), StringComparer.Ordinal);

  static readonly System.Globalization.CultureInfo Inv =
    System.Globalization.CultureInfo.InvariantCulture;

  // One row per hint a `default` may be declared on: how the required JSON value
  // reads in a warning, and the goo the value rides in. Both facts live here so
  // they cannot drift — a hint that validated but had no goo would pass every
  // check and then silently seed nothing.
  //
  // The four are not an arbitrary subset. They are where three lists meet: the
  // JSON scalar kinds (true/false, number, string — nothing spells a Point3d),
  // the GH_InputParamManager overloads that take a default value, and the goo
  // types below. So the ceiling is JSON's, not Grasshopper's; widening it means
  // designing a composite spelling and teaching both parsers to read it.
  //
  // The HINT picks the goo, never the JSON kind: a whole number on a `double`
  // param must become a GH_Number, or the param hands the script an int where
  // it declared a double.
  // SYNC: gh_meta.py's DEFAULTABLE.
  class DefaultKind
  {
    public string Reads;
    public Func<object, IGH_Goo> Goo;
  }

  static readonly Dictionary<string, DefaultKind> Defaultable =
    new Dictionary<string, DefaultKind>(StringComparer.Ordinal)
  {
    { "bool",   new DefaultKind { Reads = "true or false",
        Goo = v => new GH_Boolean(Convert.ToBoolean(v, Inv)) } },
    { "int",    new DefaultKind { Reads = "a whole number",
        Goo = v => new GH_Integer(Convert.ToInt32(v, Inv)) } },
    { "double", new DefaultKind { Reads = "a number",
        Goo = v => new GH_Number(Convert.ToDouble(v, Inv)) } },
    { "string", new DefaultKind { Reads = "a string",
        Goo = v => new GH_String(Convert.ToString(v, Inv)) } },
  };

  // A header `type` names a Grasshopper type-hint CONVERTER, and Grasshopper
  // names two of them differently per language: the C# `double` converter is
  // `float` on Python, and `string` is `str`. HintCandidates resolves either
  // spelling onto the live param, so a Python header saying `float` works — but
  // the canonical spelling, the one Defaultable and the codegen's HINTS key on,
  // is the C# name. Rather than widen every table with aliases (which would put
  // the same converter under two names in three places), name the canonical
  // spelling in the one message where the difference is felt.
  // SYNC: gh_meta.py's _CANONICAL.
  static readonly Dictionary<string, string> Canonical =
    new Dictionary<string, string>(StringComparer.Ordinal)
  { { "float", "double" }, { "str", "string" } };

  // Why this param's declared default is unusable, or null when it is fine.
  // Both the warning pass and ApplyDefault read it, so Forge never seeds a
  // value it just warned about.
  // SYNC: gh_meta.py's default_problem — same verdicts, same order.
  static string DefaultProblem(HeaderParam d, bool isOutput)
  {
    if (d.Default == null) return null;

    // Grasshopper only ever collects INTO an input. An output's persistent data
    // is overwritten by the first solve, and RegisterOutputParams has no
    // value-taking overload for the compiled build to emit, so this would mean
    // two different nothings on the two surfaces.
    if (isOutput) return "an output has no default — nothing is ever collected into it";

    DefaultKind kind;
    if (!Defaultable.TryGetValue(d.Hint, out kind))
    {
      string canon;
      if (Canonical.TryGetValue(d.Hint, out canon))
        return "default is only supported on " + string.Join("/", Defaultable.Keys)
          + " — '" + d.Hint + "' is the same converter under its Python name, so spell "
          + "the type '" + canon + "'";
      return "default is only supported on " + string.Join("/", Defaultable.Keys)
        + ", not '" + d.Hint + "'";
    }

    bool ok;
    if (d.Default is bool) ok = d.Hint == "bool";
    else if (d.Default is long || d.Default is int) ok = d.Hint == "int" || d.Hint == "double";
    // 2.0 on an int param is a typo worth reporting, not a silent truncation.
    else if (d.Default is double) ok = d.Hint == "double";
    else ok = d.Hint == "string" && d.Default is string;
    return ok ? null
      : "default " + FormatDefault(d.Default) + " is not " + kind.Reads + " (" + d.Hint + " param)";
  }

  // Invariant culture throughout: a warning that reads `1,5` under a European
  // locale would not match the header the user is looking at. A RawJson falls
  // through to its ToString, i.e. back to the bytes the header actually held.
  static string FormatDefault(object v)
  {
    if (v is bool) return (bool)v ? "true" : "false";
    if (v is string) return "'" + v + "'";
    return Convert.ToString(v, Inv);
  }

  static void WarnDriftAndQuotes(string text, HeaderMeta meta, bool isPython, List<string> log)
  {
    // Findings the parse itself collected — an unrecognized key, which is
    // ignored by design but must not be ignored in silence.
    foreach (var w in meta.Warnings) log.Add("KEY WARNING: " + w);

    if (meta.Desc != null && meta.Desc.Contains("\""))
      log.Add("QUOTE WARNING: component description contains a double quote — breaks the ScriptEditor plugin builder if published");
    foreach (var d in meta.Ins.Concat(meta.Outs))
    {
      if (d.Desc.Contains("\""))
        log.Add("QUOTE WARNING: description of param " + d.VariableName + " contains a double quote");
      // Both label slots reach a generated C# literal the same way a description
      // does — Name as the compiled component's Name, Nickname as its NickName —
      // so the same ban applies. See gh_meta.py's check_meta.
      if (d.Name != null && d.Name.Contains("\""))
        log.Add("QUOTE WARNING: name of param " + d.VariableName + " contains a double quote");
      if (d.Nickname != null && d.Nickname.Contains("\""))
        log.Add("QUOTE WARNING: nickname of param " + d.VariableName + " contains a double quote");
    }

    // Two params on the SAME side sharing a LABEL compile to a component with
    // two identically-labelled params, and Name is additionally what
    // DA.GetData(name) resolves against. Across sides it is the whole point.
    // The two slots report together when they hold the same string, which is
    // the common case — a param that spelled out neither.
    foreach (var side in new[] { meta.Ins, meta.Outs })
      for (int i = 0; i < side.Count; i++)
        for (int j = i + 1; j < side.Count; j++)
        {
          if (side[i].VariableName == side[j].VariableName) continue;
          var shared = new List<string>();
          if (side[i].Name == side[j].Name) shared.Add("name " + side[j].Name);
          if (side[i].Nickname == side[j].Nickname) shared.Add("nickname " + side[j].Nickname);
          if (shared.Count == 2 && side[j].Name == side[j].Nickname)
            shared = new List<string> { "name and nickname " + side[j].Name };
          if (shared.Count > 0)
            log.Add("LABEL WARNING: params " + side[i].VariableName + " and " + side[j].VariableName
              + " share the " + string.Join(" and the ", shared));
        }

    // VariableName becomes this param's NickName. Rhino stores anything at all
    // in the NickName slot without validating it, so this is the only guard
    // there is. It must always be unique within its own side, both languages:
    // two same-named params on one side can't be told apart, and Grasshopper
    // silently keeps only one of them when it rebuilds the live signature
    // (verified against a canvas 2026-08-26).
    //
    // For C# it must ALSO be unique ACROSS sides: an input and an output
    // become two locals in the same generated Invoke, and live, two params in
    // the same RunScript parameter list — both a hard compile error on a
    // clash. Python has neither reason: its RunScript has no fixed output
    // parameter list to collide in (outputs are read back from the exec
    // namespace by name, not declared), and a compiled build never processes a
    // .py source at all (docs/ship-a-plugin/dotnet-build.md, "Scope: C# only"). So a
    // Python input and output sharing a label — both called Keys, say — is
    // fine and common; verified working end to end on a live canvas
    // 2026-08-26 (examples/ring-array-shared-labels.py).
    var crossSeen = new Dictionary<string, string>();
    foreach (var side in new[] { new { Defs = meta.Ins, Label = "input" },
                                 new { Defs = meta.Outs, Label = "output" } })
    {
      var sideSeen = new Dictionary<string, string>();
      foreach (var d in side.Defs)
      {
        if (!ReIdentifier.IsMatch(d.VariableName))
          log.Add("NAME WARNING: variable name '" + d.VariableName + "' is not an identifier");
        else if (!isPython && CsKeywords.Contains(d.VariableName))
          log.Add("NAME WARNING: variable name '" + d.VariableName + "' is a C# keyword");
        if (sideSeen.ContainsKey(d.VariableName))
          log.Add("NAME WARNING: duplicate " + side.Label + " variable name '" + d.VariableName
            + "' — Grasshopper can't tell the two params apart");
        else
          sideSeen[d.VariableName] = side.Label;
        if (!isPython)
        {
          string first;
          if (crossSeen.TryGetValue(d.VariableName, out first))
            log.Add("NAME WARNING: duplicate variable name '" + d.VariableName + "' (" + first
              + " and " + side.Label + ") — both become locals in one generated Invoke");
          else
            crossSeen[d.VariableName] = side.Label;
        }

        // `optional` and `default`, which only a JSON header can spell. Both
        // are input-side ideas — see DefaultProblem. A param that warns here is
        // configured without its default; the rest of the forge proceeds.
        var problem = DefaultProblem(d, side.Label == "output");
        if (problem != null)
          log.Add("DEFAULT WARNING: " + d.VariableName + ": " + problem);
        if (side.Label == "output" && !d.Optional)
          log.Add("OPTIONAL WARNING: " + d.VariableName
            + ": 'optional' is an input-side idea — an output is never collected into");
      }
    }

    if (isPython) return;
    List<string> sigIns, sigOuts;
    if (!TryParseSignatureNames(text, out sigIns, out sigOuts)) return;
    // The signature declares VARIABLE names — the C# identifiers.
    foreach (var d in meta.Ins)
      if (!sigIns.Contains(d.VariableName))
        log.Add("DRIFT WARNING: header input " + d.VariableName + " is not in the RunScript signature");
    foreach (var n in sigIns)
      if (meta.Ins.All(x => x.VariableName != n))
        log.Add("DRIFT WARNING: RunScript input " + n + " is not in the header");
    foreach (var d in meta.Outs)
      if (!sigOuts.Contains(d.VariableName))
        log.Add("DRIFT WARNING: header output " + d.VariableName + " is not in the RunScript signature");
    foreach (var n in sigOuts)
      if (meta.Outs.All(x => x.VariableName != n))
        log.Add("DRIFT WARNING: RunScript output " + n + " is not in the header");
  }

  static bool TryParseSignatureNames(string text, out List<string> ins, out List<string> outs)
  {
    ins = new List<string>();
    outs = new List<string>();
    var m = Regex.Match(text, @"void\s+RunScript\s*\(");
    if (!m.Success) return false;
    int i = m.Index + m.Length, depth = 1, start = i;
    while (i < text.Length && depth > 0)
    {
      if (text[i] == '(') depth++;
      else if (text[i] == ')') depth--;
      i++;
    }
    if (depth != 0) return false;
    string body = text.Substring(start, i - 1 - start);

    int d = 0, last = 0;
    var parts = new List<string>();
    for (int k = 0; k < body.Length; k++)
    {
      char c = body[k];
      if (c == '<' || c == '(' || c == '[') d++;
      else if (c == '>' || c == ')' || c == ']') d--;
      else if (c == ',' && d == 0) { parts.Add(body.Substring(last, k - last)); last = k + 1; }
    }
    parts.Add(body.Substring(last));

    foreach (var raw in parts)
    {
      var t = raw.Trim();
      if (t.Length == 0) continue;
      bool isOut = t.StartsWith("out ") || t.StartsWith("ref ");
      if (isOut) t = t.Substring(4).Trim();
      var nm = Regex.Match(t, @"([A-Za-z_][A-Za-z0-9_]*)\s*$");
      if (!nm.Success || nm.Index == 0) continue; // need both a type and a name
      (isOut ? outs : ins).Add(nm.Groups[1].Value);
    }
    return true;
  }

  // ------------------------------------------------------ language detect --

  static bool DetectPython(string text, HeaderMeta meta, IGH_Component existing)
  {
    if (meta != null && meta.Language != null)
    {
      var l = meta.Language.ToLowerInvariant();
      if (l == "python" || l == "python3" || l == "py") return true;
      if (l == "csharp" || l == "cs" || l == "c#") return false;
      throw new Exception("unrecognized header language field: " + meta.Language);
    }
    foreach (var line in text.Split('\n').Take(5))
    {
      var t = line.Trim();
      if (t.StartsWith("#!") && t.ToLowerInvariant().Contains("python")) return true;
    }
    if (meta != null)
    {
      if (meta.OpenerStyle == "cs") return false;
      if (meta.OpenerStyle == "py") return true;
    }
    else if (existing != null)
    {
      // headerless update: the target component knows its own language
      return existing.GetType().FullName == PyComponentType;
    }
    if (text.Contains("Script_Instance") || Regex.IsMatch(text, @"^\s*using\s+[\w.]+\s*;", RegexOptions.Multiline))
      return false;
    if (text.TrimStart().StartsWith("\"\"\"") || Regex.IsMatch(text, @"^\s*(import|from)\s+\w", RegexOptions.Multiline))
      return true;
    throw new Exception("cannot detect language — add a language field (csharp or python) to an @component header");
  }

  // ---------------------------------------------------------------- parser --
  // The @component body is one JSON object: the first non-whitespace character
  // after the marker is `{`, and its own matching brace terminates the header.
  // C# block-comment, Python docstring and # comment prefixes come off first.
  // SYNC: gh-script-kit's tooling/gh_meta.py is the other implementation of this
  // grammar and is what the compiled build's codegen parses headers with — keep
  // ParseJsonHeader/StripComment in step with it, and the rules
  // WarnDriftAndQuotes warns about in step with its check_meta (Forge warns
  // where --check fails; both must agree on what is wrong).

  class HeaderParam
  {
    // VariableName is the C# identifier — a live script param's NickName, which
    // Rhino also validates as an identifier. Name is PrettyName (the tooltip
    // title). Nickname is what a COMPILED build draws and never reaches a
    // script component at all. Each of the latter two defaults from Name.
    public string VariableName, Name, Nickname, Hint, Access, Desc;
    public bool Optional = true;
    public object Default;   // null = no declared default
  }

  class HeaderMeta
  {
    public string Name, Nick, Desc, Icon, Language, OpenerStyle;
    public Guid PinnedGuid = Guid.Empty;
    public bool SyncParams = true;
    public List<HeaderParam> Ins = new List<HeaderParam>();
    public List<HeaderParam> Outs = new List<HeaderParam>();
    // Non-fatal findings from the parse itself — an unrecognized key. Emitted
    // by WarnDriftAndQuotes with everything else, so the Log is one surface.
    public List<string> Warnings = new List<string>();
  }

  static HeaderMeta ParseHeader(string text)
  {
    var lines = text.Replace("\r\n", "\n").Split('\n');
    int start = -1;
    string opener = null;
    for (int i = 0; i < lines.Length; i++)
    {
      if (StripComment(lines[i]).StartsWith("@component"))
      {
        start = i;
        var raw = lines[i].Trim();
        opener = raw.StartsWith("/*") || raw.StartsWith("//") ? "cs"
               : raw.StartsWith("\"\"\"") || raw.StartsWith("#") ? "py"
               : null;
        break;
      }
    }
    if (start < 0) return null; // headerless source

    // The body must open with `{`. Saying so out loud is worth the four lines:
    // any other body reaches the JSON reader and comes back as a byte-offset
    // syntax error, which tells someone holding one nothing useful.
    var first = StripComment(lines[start]).Substring("@component".Length).TrimStart();
    for (int i = start + 1; first.Length == 0 && i < lines.Length; i++)
      first = StripComment(lines[i]);
    if (!first.StartsWith("{", StringComparison.Ordinal))
      throw new Exception("@component body must be a JSON object opening with '{' — the "
        + "key: value / @in / @out line grammar is retired and has no converter; "
        + "rewrite the header by hand");
    return ParseJsonHeader(lines, start, opener);
  }

  // Access is matched case-insensitively and stored canonical (JsonParams
  // lowers it), so everything downstream compares against item/list/tree only.
  // SYNC: gh_meta.py's ACCESS_MODES membership test.
  static bool IsAccess(string s)
  {
    return s == "item" || s == "list" || s == "tree";
  }

  // Every key the grammar knows, at the component level and inside a param
  // object. A key outside these is still ignored — that is the
  // forward-compatibility promise and it does not change — but being ignored
  // SILENTLY is how a typo'd "nickanme" costs an hour, so JsonKeys logs one.
  // `guid` counts as known only so it is not reported twice: it has its own
  // hard rejection below, with a message that says what to write instead.
  // Several of these (category, exposure, componentGuid, markers, upgradeFrom)
  // mean nothing to a forge and are read by gh_codegen alone — they are listed
  // because they are valid GRAMMAR, not because this parser uses them.
  // SYNC: gh_meta.py's _JSON_COMPONENT_KEYS / _JSON_PARAM_KEYS.
  static readonly HashSet<string> ComponentKeys = new HashSet<string>(
    new[] { "name", "nickname", "description", "category", "subcategory",
            "icon", "language", "exposure", "instanceGuid", "componentGuid",
            "markers", "upgradeFrom", "inputs", "outputs", "guid" }
      .Select(k => k.ToLowerInvariant()));

  static readonly HashSet<string> ParamKeys = new HashSet<string>(
    new[] { "name", "variableName", "nickname", "type", "access",
            "description", "optional", "default" }
      .Select(k => k.ToLowerInvariant()));

  // A JSON object re-keyed to lower case, so a header may spell variableName,
  // VariableName or variablename and reach the same slot. The documented
  // spelling stays canonical — this only decides what MATCHES it.
  //
  // Two keys of one object that differ only in case are a mistake JSON permits
  // — "name" and "Name" together says nothing about which was meant — so they
  // throw rather than one of them silently winning.
  // SYNC: gh_meta.py's _fold_keys / _Folded.
  static Dictionary<string, JsonElement> JsonKeys(
    JsonElement o, string where, HashSet<string> known, List<string> warnings)
  {
    var folded = new Dictionary<string, JsonElement>(StringComparer.Ordinal);
    var spelling = new Dictionary<string, string>(StringComparer.Ordinal);
    foreach (var prop in o.EnumerateObject())
    {
      var low = prop.Name.ToLowerInvariant();
      string first;
      if (spelling.TryGetValue(low, out first))
        throw new Exception(where + ": keys '" + first + "' and '" + prop.Name
          + "' differ only in case — one object cannot carry both");
      spelling[low] = prop.Name;
      folded[low] = prop.Value;
      if (!known.Contains(low))
        warnings.Add(where + ": unknown key '" + prop.Name + "' — ignored");
    }
    return folded;
  }

  // ------------------------------------------------------------ JSON body --
  // One JSON object, opened by `{` and closed by its own matching brace, so
  // there is no terminator keyword. Every field is a named key rather than a
  // positional one, which is what makes `optional` and `default` expressible. System.Text.Json resolves from Rhino's shared framework in
  // both worlds — the GH script sandbox and the compiled .gha — with no package
  // reference; probed live on 8.33 (see the kit's docs/write-scripts/hand-rolled-json.md).

  static HeaderMeta ParseJsonHeader(string[] lines, int start, string opener)
  {
    // The comment prefixes come off line by line, which is safe because a JSON
    // string cannot span a line break — so no stripped prefix is ever part of a
    // value.
    var body = new List<string> { StripComment(lines[start]).Substring("@component".Length) };
    for (int i = start + 1; i < lines.Length; i++) body.Add(StripComment(lines[i]));

    // ParseValue off a Utf8JsonReader, not JsonDocument.Parse: it reads ONE
    // value and stops, so whatever follows the closing brace (a `*/`, a `"""`,
    // the rest of the file) is not trailing-content garbage. The reader skips
    // the leading whitespace the marker line left behind. The Python side gets
    // the same behaviour from json.JSONDecoder().raw_decode.
    var bytes = System.Text.Encoding.UTF8.GetBytes(string.Join("\n", body));
    JsonDocument doc;
    try
    {
      var reader = new Utf8JsonReader(bytes);
      doc = JsonDocument.ParseValue(ref reader);
    }
    catch (JsonException ex)
    {
      throw new Exception("@component header is not valid JSON: " + ex.Message);
    }

    // Everything is materialized inside the using: a JsonElement is a window
    // onto the document's pooled buffer and is undefined once it is disposed.
    using (doc)
    {
      var root = doc.RootElement;
      if (root.ValueKind != JsonValueKind.Object)
        throw new Exception("@component header must be a JSON object");

      var meta = new HeaderMeta { OpenerStyle = opener };
      var obj = JsonKeys(root, "header", ComponentKeys, meta.Warnings);
      meta.Name = JsonRequiredString(obj, "name", "header");
      meta.Desc = JsonRequiredString(obj, "description", "header");
      meta.Nick = JsonString(obj, "nickname", "header") ?? meta.Name;
      meta.Icon = JsonString(obj, "icon", "header");
      meta.Language = JsonString(obj, "language", "header");
      // `guid` was the line grammar's one key for two unrelated properties: the
      // canvas InstanceGuid here, the published ComponentGuid in the codegen.
      // Unknown keys are otherwise ignored, and being ignored is exactly what
      // must not happen here — a header still saying `guid` would silently stop
      // pinning its target. SYNC: gh_meta.py's _parse_json_header raises the same.
      if (obj.ContainsKey("guid"))
        throw new Exception("header 'guid' is retired — say 'instanceGuid' (which component "
          + "on the canvas to update) or 'componentGuid' (the permanent published identity), "
          + "or both");

      // Forge targets an INSTANCE; componentGuid is the compiled build's
      // permanent identity and means nothing here.
      var guidField = JsonString(obj, "instanceGuid", "header");
      if (guidField != null)
      {
        Guid g;
        if (Guid.TryParse(guidField, out g)) meta.PinnedGuid = g;
        else throw new Exception("header instanceGuid is not a valid guid");
      }
      meta.Ins.AddRange(JsonParams(obj, "inputs", meta.Warnings));
      meta.Outs.AddRange(JsonParams(obj, "outputs", meta.Warnings));
      return meta;
    }
  }

  static List<HeaderParam> JsonParams(
    Dictionary<string, JsonElement> root, string key, List<string> warnings)
  {
    var list = new List<HeaderParam>();
    JsonElement arr;
    if (!root.TryGetValue(key.ToLowerInvariant(), out arr)
        || arr.ValueKind == JsonValueKind.Null) return list;
    if (arr.ValueKind != JsonValueKind.Array)
      throw new Exception("header " + key + " must be an array of param objects");

    int i = 0;
    foreach (var e in arr.EnumerateArray())
    {
      var where = key + "[" + i++ + "]";
      if (e.ValueKind != JsonValueKind.Object) throw new Exception(where + " is not an object");
      var p = JsonKeys(e, where, ParamKeys, warnings);

      var name = JsonRequiredString(p, "name", where);
      var declared = JsonRequiredString(p, "access", where);
      var access = declared.ToLowerInvariant();
      if (!IsAccess(access))
        throw new Exception("bad access '" + declared + "' for param " + name
          + " — expected item/list/tree");

      JsonElement opt;
      bool optional = true;
      if (p.TryGetValue("optional", out opt) && opt.ValueKind != JsonValueKind.Null)
      {
        if (opt.ValueKind != JsonValueKind.True && opt.ValueKind != JsonValueKind.False)
          throw new Exception(where + ": optional must be true or false");
        optional = opt.ValueKind == JsonValueKind.True;
      }

      JsonElement def;
      object defaultValue = null;
      if (p.TryGetValue("default", out def) && def.ValueKind != JsonValueKind.Null)
        defaultValue = JsonScalar(def);

      // The fan: VariableName and Nickname each default from Name on their own.
      // Chaining would let a short compiled NickName become the C# identifier.
      list.Add(new HeaderParam
      {
        VariableName = JsonString(p, "variableName", where) ?? name,
        Name = name,
        Nickname = JsonString(p, "nickname", where) ?? name,
        Hint = JsonRequiredString(p, "type", where),
        Access = access,
        Desc = JsonString(p, "description", where) ?? "",
        Optional = optional,
        Default = defaultValue,
      });
    }
    return list;
  }

  // Absent or explicitly null reads as null; a non-string of any other kind is
  // an error rather than a silent ToString().
  // `key` is the CANONICAL spelling and is lowered here, so no caller has to
  // remember that the map is folded.
  static string JsonString(Dictionary<string, JsonElement> o, string key, string where)
  {
    JsonElement e;
    if (!o.TryGetValue(key.ToLowerInvariant(), out e) || e.ValueKind == JsonValueKind.Null)
      return null;
    if (e.ValueKind != JsonValueKind.String)
      throw new Exception(where + " " + key + " must be a string");
    return e.GetString();
  }

  static string JsonRequiredString(Dictionary<string, JsonElement> o, string key, string where)
  {
    var v = JsonString(o, key, where);
    if (v == null) throw new Exception(where + " missing required " + key);
    return v;
  }

  // A declared default, boxed as the CLR type its JSON kind implies. An object
  // or array is no usable default for any supported hint, but it must NOT come
  // back as null: null means "no default declared", and collapsing the two would
  // have Forge quietly ignore a header that gh_meta --check fails — the reverse
  // of the rule that Forge warns wherever --check fails. RawJson keeps it a
  // declared-but-unusable value that DefaultProblem rejects out loud.
  static object JsonScalar(JsonElement e)
  {
    switch (e.ValueKind)
    {
      case JsonValueKind.True: return true;
      case JsonValueKind.False: return false;
      case JsonValueKind.String: return e.GetString();
      case JsonValueKind.Number:
        long l;
        return e.TryGetInt64(out l) ? (object)l : e.GetDouble();
      default: return new RawJson(e.GetRawText());
    }
  }

  // The raw bytes of a declared value that is not a JSON scalar. Captured
  // eagerly: a JsonElement is only valid while its JsonDocument lives, and
  // ParseJsonHeader disposes that before the meta is ever read.
  class RawJson
  {
    readonly string _text;
    public RawJson(string text) { _text = text; }
    public override string ToString() { return _text; }
  }

  static string StripComment(string line)
  {
    var t = line.Trim();
    if (t.StartsWith("/*")) t = t.Substring(2).Trim();
    if (t.StartsWith("\"\"\"")) t = t.Substring(3).Trim();
    if (t.StartsWith("//")) t = t.Substring(2).Trim();
    else if (t.StartsWith("#")) t = t.TrimStart('#').Trim();
    return t;
  }
}
