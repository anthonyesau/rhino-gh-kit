// build-forge-rig.cs — build the standing forge rig on the active canvas.
//
// NOT a Grasshopper script component. This is a payload for
// `mcp__rhino__run_csharp`, run against the live, open Grasshopper document.
//
// The rig is the minimal thing that forges a `.cs`/`.py` file onto the canvas:
// a Source panel, a Target value list, the compiled Script Forge, a Run button,
// and a group around them, wired. Canvases are gitignored, so this file is the
// rig's definition — run it to get one, rather than hunting for a saved `.gh`.
//
// Usage:
//   1. Open the canvas you want the rig on.
//   2. Paste this whole file into mcp__rhino__run_csharp.
//   3. Read the printed InstanceGuids — freshly minted every run.
//
// Save the canvas at the repo root if you want it to persist. Root matters: a
// rig there is what makes an example's bare `icons/<name>.svg` header come up
// one segment short and log the expected `missing icon` warning. See CLAUDE.md,
// "Environment".
//
// Re-running adds a SECOND rig rather than updating the first — the objects
// carry no durable identity to match on. Clear the canvas first.
//
// Requires Script Forge to be installed. If it is not, the run stops and adds
// nothing: a rig without a forge is not worth leaving behind.

using System;
using System.Drawing;
using System.Linq;
using Grasshopper;
using Grasshopper.Kernel;
using Grasshopper.Kernel.Special;

// The compiled plugin's ComponentGuid — see CLAUDE.md, "Two identities are
// pinned and must not be regenerated".
var FORGE_PROXY = new Guid("41822538-1827-4da2-bf84-58074c49b3ad");

var ghDoc = Instances.ActiveCanvas == null ? null : Instances.ActiveCanvas.Document;
if (ghDoc == null)
{
  Console.WriteLine("no active Grasshopper document — open one first");
  return;
}

// --- The forge -------------------------------------------------------------
// Emitted from the component server, which is what dropping it from the ribbon
// does. A null means the plugin is not loaded.
var forgeObj = Instances.ComponentServer.EmitObject(FORGE_PROXY);
if (forgeObj == null)
{
  Console.WriteLine("Script Forge is not installed (proxy " + FORGE_PROXY + " emitted nothing).");
  Console.WriteLine("Install it: tooling/publish.sh --repo script-forge install, then restart Rhino.");
  return;
}
var forge = (IGH_Component) forgeObj;
ghDoc.AddObject(forge, false);

// --- Source panel ----------------------------------------------------------
// Multiline OFF is deliberate: it makes GH split the panel text into one item
// per line, which is how a multi-path Source fans out to one component per path.
var panel = new GH_Panel();
panel.CreateAttributes();
panel.NickName = "Source Path";
panel.UserText = "";
panel.Properties.Multiline = false;
// Bounds contributes the width — wide enough for a path without wrapping. The
// height snaps to the panel's own minimum, and the position is set below.
panel.Attributes.Bounds = new RectangleF(0f, 0f, 230f, 36f);
ghDoc.AddObject(panel, false);

// --- Target value list -----------------------------------------------------
// Expressions are GH expression strings, so the keywords carry their own quotes
// and Create emits a literal null.
var targets = new GH_ValueList();
targets.CreateAttributes();
targets.NickName = "Target";
targets.ListMode = GH_ValueListMode.DropDown;
targets.ListItems.Clear();
targets.ListItems.Add(new GH_ValueListItem("Create", "null"));
targets.ListItems.Add(new GH_ValueListItem("Name", "\"name\""));
targets.ListItems.Add(new GH_ValueListItem("Nickname", "\"nickname\""));
targets.ListItems.Add(new GH_ValueListItem("Match name, create missing", "\"name+create\""));
targets.ListItems.Add(new GH_ValueListItem("Match nickname, create missing", "\"nickname+create\""));
// name+create: the first run creates the component, every later run updates that
// same one in place instead of littering the canvas with copies.
targets.SelectItem(3);
ghDoc.AddObject(targets, false);

// --- Run button ------------------------------------------------------------
// A button, not a toggle: Run is edge-triggered, and a latched toggle re-forges
// on every unrelated solve.
var button = new GH_ButtonObject();
button.CreateAttributes();
button.NickName = "Run Forge";
ghDoc.AddObject(button, false);

// --- Wire it ---------------------------------------------------------------
// Three wires, which is every input the component has. Addressed by param Name,
// so a param reshuffle in a future forge version fails loudly here rather than
// wiring the wrong slot.
Action<string, IGH_Param> wire = (name, src) =>
{
  var dst = forge.Params.Input.FirstOrDefault(p =>
    string.Equals(p.Name, name, StringComparison.OrdinalIgnoreCase));
  if (dst == null)
  {
    Console.WriteLine("  ! no input named '" + name + "' — inputs are: "
      + string.Join(", ", forge.Params.Input.Select(p => p.Name)));
    return;
  }
  dst.AddSource(src);
  Console.WriteLine("  wired " + src.NickName + " -> " + name);
};

wire("Source", panel);
wire("Target", targets);
wire("Run", button);

// --- Lay it out ------------------------------------------------------------
// Position by BOUNDS, not by Pivot. Every object type anchors its pivot
// differently — a component's bounds centre on it, a panel's start at it, a
// value list's sit left of it — so hard-coded pivots give a ragged layout, and a
// panel positioned by Bounds alone snaps back to the canvas origin because
// layout recomputes X/Y from the pivot. Nudging the pivot by (want - have) puts
// the visible rectangle exactly where asked, for every type.
Action<IGH_DocumentObject, float, float> place = (o, x, y) =>
{
  o.Attributes.PerformLayout();
  var b = o.Attributes.Bounds;
  var p = o.Attributes.Pivot;
  o.Attributes.Pivot = new PointF(p.X + (x - b.X), p.Y + (y - b.Y));
  o.Attributes.ExpireLayout();
  o.Attributes.PerformLayout();
};

const float COL_RIGHT = 340f;   // shared right edge of the input column
const float TOP       = 160f;
const float VGAP      = 14f;
const float HGAP      = 60f;    // column to forge

// TWO passes, not one. A freshly constructed object reports a provisional width
// until it has been laid out once in a real document — a button says 99 before
// and 116 after, which is enough to push it 17px past a right-aligned edge. The
// first pass settles the widths; the second uses them. Both are cheap.
for (int pass = 0; pass < 2; pass++)
{
  // Right-align the three inputs into a column, so their output nubs line up
  // and the wires run level into the forge.
  float y = TOP;
  foreach (var o in new IGH_DocumentObject[] { panel, targets, button })
  {
    o.Attributes.PerformLayout();
    place(o, COL_RIGHT - o.Attributes.Bounds.Width, y);
    y = o.Attributes.Bounds.Bottom + VGAP;
  }

  // Forge to the right, vertically centred on the column.
  forge.Attributes.PerformLayout();
  place(forge, COL_RIGHT + HGAP, (TOP + (y - VGAP)) / 2f - forge.Attributes.Bounds.Height / 2f);
}

// --- Group it --------------------------------------------------------------
// Added last: the group sizes itself to its members, so they must be in place.
var group = new GH_Group();
group.CreateAttributes();
group.NickName = "Forge";
group.Colour = Color.FromArgb(60, 130, 160, 200);
group.AddObject(forge.InstanceGuid);
group.AddObject(panel.InstanceGuid);
group.AddObject(targets.InstanceGuid);
group.AddObject(button.InstanceGuid);
group.ExpireCaches();
ghDoc.AddObject(group, false);

// --- Report ----------------------------------------------------------------
Console.WriteLine("");
Console.WriteLine("| role        | type             | nickname    | InstanceGuid |");
Console.WriteLine("|-------------|------------------|-------------|--------------|");
Console.WriteLine("| source      | GH_Panel         | Source Path | " + panel.InstanceGuid + " |");
Console.WriteLine("| target mode | GH_ValueList     | Target      | " + targets.InstanceGuid + " |");
Console.WriteLine("| the forge   | Comp_ScriptForge | Script Forge| " + forge.InstanceGuid + " |");
Console.WriteLine("| trigger     | GH_ButtonObject  | Run Forge   | " + button.InstanceGuid + " |");
Console.WriteLine("| --          | GH_Group         | Forge       | " + group.InstanceGuid + " |");

// Deferred, never inline: expiring an object mid-solution trips Grasshopper 8's
// "object expired during a solution" guard and locks the canvas. See
// docs/write-scripts/rhino-mcp-platform.md, constraint 2.
ghDoc.ScheduleSolution(5, d => d.NewSolution(false));
