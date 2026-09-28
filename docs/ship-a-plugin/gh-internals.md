# Grasshopper internals for canvas tools

A compiled plugin that does more than register components — one that reaches into
clusters, swaps another object's attributes, or adds to the canvas's menus — runs into
Grasshopper behaviour the SDK does not document. This doc collects what has been
established about it.

Measured on **Rhino / Grasshopper 8.35.26251, macOS arm64**, with the internals read
from `Grasshopper.dll` decompiled (see [Reading Grasshopper's source](#reading-grasshoppers-source)).
Private members named here are not API: re-check them after a Rhino update.

- ✅ — measured live, driving the real code path (dialogs answered by a person)
- 📖 — read in the decompiled source: the mechanism behind a measured effect

## Clusters

A cluster (`GH_Cluster`) wraps a private inner document, `m_internalDocument`, whose
Cluster Input / Output hooks (`GH_ClusterInputHook` / `GH_ClusterOutputHook`) become
the cluster component's params. Copies sharing a `DocumentId` are **entangled**: an
edit committed to one is meant to reach them all.

### Renaming a hook never reaches the cluster's params

✅ Rename a hook inside a cluster and commit it (Save & Close, or `DocumentModified`
from code), and the cluster component keeps the old param names on every entangled
copy — measured through the editor's real Save & Close. Saving and reopening the
file does not fix it either.

`GH_Cluster.UpdateDocument` 📖 rebuilds the params but **reuses** every param whose
hook still exists, found through the private `m_mapping` (a param ↔ hook
`HookParamMap`), and copies no names. Hook → param names are copied only by
`CreateInput` / `CreateOutput`, which run for new hooks only. `GH_Cluster.Read` calls
`UpdateDocument` too, so reopening the file does not fix it.

**Fix** ✅ — copy the names across yourself. Once copied, they survive a save and
reopen, because the reused params keep them:

```csharp
var BF = BindingFlags.NonPublic | BindingFlags.Instance;
var inner = (GH_Document)typeof(GH_Cluster).GetField("m_internalDocument", BF).GetValue(cluster);
var map = typeof(GH_Cluster).GetField("m_mapping", BF).GetValue(cluster);
var getHook = map.GetType().GetMethods()
    .First(m => m.Name == "get_Hook" && m.GetParameters()[0].ParameterType == typeof(Guid));
foreach (var p in cluster.Params.Input.Concat(cluster.Params.Output))
{
    var hook = inner.FindObject((Guid)getHook.Invoke(map, new object[] { p.InstanceGuid }), false);
    if (hook == null) continue;
    p.Name = hook.Name; p.NickName = hook.NickName; p.Description = hook.Description;
}
cluster.Attributes.ExpireLayout();
cluster.OnPingDocument()?.DestroyAttributeCache();
```

**To rename a hook from code, set `CustomName` and `CustomNickName`** ✅ (and
`CustomDescription`). The hook's `Name` / `NickName` / `Description` getters return
the custom value when one is set, else the connected param's. The plain setters write
a base field that the getter ignores while a custom value exists, so they appear to do
nothing.

### Entanglement reaches only one document's top level

✅ A committed edit (`GH_Cluster.DocumentModified`) updates the copies returned by
`OwnerDocument.FindClusters(DocumentId)` 📖, a non-recursive scan of the owning
document's top-level objects. Entangled copies nested inside other clusters, or in
other open documents, keep their old contents and silently diverge.

**Workaround** ✅ — do what `DocumentModified` does for its own family. Write the
committed inner document into a `GH_LooseChunk` once, and give each other copy a
fresh `GH_Document` read from it through the public `UpdateDocument`. Then re-sync
the param names, because `UpdateDocument` reuses params (above).

```csharp
var chunk = new GH_LooseChunk("Document");
committedInner.Write(chunk);
foreach (var copy in strayCopies)
{
    var fresh = new GH_Document();
    fresh.Read(chunk);
    copy.UpdateDocument(fresh);
    // then copy hook names onto the params, as above
}
```

**Comparing serialized bytes is a sound "already in sync" test** ✅
(`chunk.Serialize_Binary()`). Serializing an inner document is deterministic, and
every copy rebuilt by a commit serializes byte-identical. A copy just pasted with
`MutateAllIds` can differ from its original until the next commit, so read a mismatch
as "resync", never as "diverged" — a false mismatch costs only a redundant resync.

### A cluster commit raises no event of its own

✅ The only signal is the undo record `DocumentModified` pushes after rebuilding the
family, named **`"Cluster Change"`**. Subscribe to `GH_Document.UndoStateChanged` on
every server document — cluster-editor documents included — and filter on
`e.Operation == GH_UndoOperation.RecordAdded` and `e.Record.Name == "Cluster Change"`.

The record holds one `GH_GenericObjectAction` per rebuilt cluster. Its target is the
private field `m_object_id` on `GH_GenericObjectAction`; resolve it with
`doc.FindObject(id, false)`.

`UndoStateChanged` for `Undo` / `Redo` fires **after** the record has been applied ✅
(`GH_UndoServer.PerformUndo`), so a handler sees the restored state.

### Removing a cluster from a document destroys its contents

✅ `GH_Cluster.RemovedFromDocument` disposes `m_internalDocument` and nulls it.
`tempDoc.RemoveObject(c)` followed by `target.AddObject(c)` leaves a cluster with no
contents and `DocumentId == Guid.Empty`. To move objects between documents use
`target.MergeDocument(source)`, which is how Grasshopper's own paste does it.

### Copy, paste and entanglement from code

✅ Each measured live:

- **`GH_ClipboardType.Local` belongs to one `GH_DocumentIO` instance** (the instance
  field `m_localClipboard`). `Copy` and `Paste` must go through the same instance —
  a second instance's `Paste` returns `false`. The system clipboard is left alone.
- **`MutateAllIds()` keeps each cluster's `DocumentId`**, so pasted copies stay
  entangled with their original.
- **To disentangle, assign `GH_Cluster.DocumentId`** (public setter).
  `GH_Document.SetDocumentID` is internal.

### Reading a cluster's contents without UI

✅ `GH_Cluster.Document(password)` returns `null` for a password-protected cluster
unless given the right password; it never prompts (`RequestPassword` is the method
that shows a dialog). To read contents regardless of a password, read the private
`m_internalDocument`.

## Canvas UI

### Adding context-menu items to objects you don't own

Grasshopper has no extension point for another object's context menu:
`GH_Canvas.Canvas_MouseUp` 📖 builds it inline from `DocObject.AppendMenuItems`. Before
that, though, it offers the mouse-up to the object's attributes
(`MouseUp_InactiveObject` → `RespondToMouseUp`), and a `GH_ObjectResponse.Handled`
result ends the event.

**Recipe** ✅ — swap in a subclass of the object's own attributes class (for clusters,
`GH_ClusterAttributes`, which is public and unsealed) and override
`RespondToMouseUp`. On a context click:

1. Build the menu the way the canvas does: `AppendMenuItems` into a
   `GH_CanvasMenuStrip`. That class is internal, so create it with
   `Activator.CreateInstance(type, nonPublic: true)`. Its `ProcessCmdKey` routes
   Enter to the item under focus, which is what commits an edit in the nickname box
   ✅. Each character typed there records its own undo.
2. Insert your items.
3. Show the menu and return `Handled`.

```csharp
public override GH_ObjectResponse RespondToMouseUp(GH_Canvas sender, GH_CanvasMouseEvent e)
{
    bool context = e.Button == MouseButtons.Right
        || (e.Button == MouseButtons.Left && Control.ModifierKeys == Keys.MacControl);
    if (!context) return base.RespondToMouseUp(sender, e);
    var type = typeof(GH_Canvas).Assembly.GetType("Grasshopper.GUI.Canvas.GH_CanvasMenuStrip");
    var menu = (ContextMenuStrip)Activator.CreateInstance(type, nonPublic: true);
    Owner.AppendMenuItems(menu);
    GH_DocumentObject.Menu_AppendSeparator(menu);
    GH_DocumentObject.Menu_AppendItem(menu, "My item", OnMyItem);
    menu.Show(sender, e.ControlLocation);
    return GH_ObjectResponse.Handled;
}
```

The resulting menu is the stock one (Enabled, Bake…, Edit Cluster…, … Help…) with
the new items after it.

**A context click**, as the canvas tests it ✅: `MouseButtons.Right`, or
`MouseButtons.Left` with `Control.ModifierKeys == Keys.MacControl` — a real Ctrl-click
arrives exactly so. The decompiled source shows the modifier as the literal `524288`.

**When swapping attributes, re-link every param** ✅ —
`p.Attributes = new GH_LinkedParamAttributes(p, newAttrs)`. Layout alone does not show
the need: the component positions its params itself, so they draw in the right
place either way. But an un-relinked param's `GetTopLevel` is still the discarded
attributes, so anything walking from a param up to its component — selection state
among it — reaches an orphan. Carry over `Pivot` and `Selected` too.

**Attributes are not serialized by type** ✅: a document written and read back holds
plain `GH_ClusterAttributes`, so a saved file carries no trace of the swap. Re-apply
it on all three of these ✅:

- `DocumentServer.DocumentAdded` — a document arriving in the server (a file
  opening, the cluster editor opening) raises this and no per-object event;
- `GH_Document.ObjectsAdded` — `AddObject`, paste (`MergeDocument`) and undoing a
  delete each raise it;
- `GH_Canvas.DocumentChanged` — fires as the canvas switches to and from a cluster
  editor; a backstop for the other two.

### Menus and dialogs block the call that opens them

✅ Rhino's macOS WinForms shim implements `ToolStripDropDown.Show` as a native
`NSMenu` pop-up, and Eto's `ShowModal` runs a modal session; both are synchronous.
A `run_csharp` payload that opens either waits until a person closes it, and the
Platform gives up on the call after 300 s while Rhino goes on waiting. Neither stops
Rhino answering: a later `run_csharp` call runs on the UI thread **nested inside the
modal loop**, with the half-finished operation still on the stack beneath it — so
it reads state mid-operation.

They surface differently, which matters when the person at the keyboard is in
another app:

| opened by the payload | appears | closes when |
|---|---|---|
| a context menu (`NSMenu`) | above every app, over the canvas | an item is chosen, Enter in the name box, Escape, or a click anywhere else |
| an Eto dialog (a password prompt, the "save changes?" prompt) | **behind** the frontmost app | a person switches to Rhino and answers it |

The corollary is useful: if such a call returns, no menu or dialog is still open.

**For live tests**, give the plugin a static interceptor that receives the built
menu instead of showing it (`if (Interceptor != null) Interceptor(menu); else
menu.Show(…)`), then drive the real path through the private `Canvas_MouseUp`:

```csharp
var ctl = canvas.Viewport.ProjectPoint(centreOfTargetInCanvasSpace);
typeof(GH_Canvas).GetMethod("Canvas_MouseUp", BindingFlags.NonPublic | BindingFlags.Instance)
    .Invoke(canvas, new object[] { canvas, new MouseEventArgs(MouseButtons.Right, 1, (int)ctl.X, (int)ctl.Y, 0) });
```

The target need not be on screen. **Lay a newly added object out before
hit-testing it**: until `Attributes.PerformLayout()` runs, its `Bounds` are a stale
default at the canvas origin (150 × 20), not where its pivot says.

When a test does need a person to answer a dialog, open it from a one-shot timer
(`RhinoApp.InvokeOnUiThread` from a `System.Threading.Timer`) so the payload returns
at once, tell them to switch to Rhino, and read the outcome in a later call.

### Grasshopper's own UI handlers can be driven from code

✅ For exercising the real paths in a test:

- **`GH_Cluster.EditClusterAsSeparateDocument()`** (public) opens the cluster
  editor: the canvas switches to a new server document whose `Owner` is the cluster,
  keeping the hooks' `InstanceGuid`s.
- **`GH_Canvas.MenuSaveClusterReturnToParentClicked(null, EventArgs.Empty)`**
  (private) is the editor's **Save & Close**. It runs `UpdateAllSubsidiaries`,
  `CloseAllSubsidiaries`, then `DocumentModified` (the `"Cluster Change"` record),
  then returns the canvas to the parent and removes the editor document. Params are
  reused, so wires survive. `DocumentModified` also **rewrites the file** when the
  cluster is linked to a `.gh`, `.ghx` or `.ghcluster` (measured for `.gh` and
  `.ghcluster`; an unlinked cluster writes nothing) — only drive it on a cluster you
  created.
- **`MenuDiscardClusterReturnToParentClicked`** is **Discard & Close**, and it asks
  first: `GH_DocumentIO.SubsidiaryDocumentSavePrompt` shows "Do you want to save the
  changes?" (No / Yes / Cancel), which blocks the call [as above](#menus-and-dialogs-block-the-call-that-opens-them).
  **No** closes the editor without committing, **Yes** is Save & Close, **Cancel**
  stays in the editor.
- **Opening the editor on a password-protected cluster prompts for the password**
  (`RequestPassword`, an Eto dialog). **Cancel** opens nothing.

**If Save & Close does rename a cluster's params, an installed canvas tool is
fixing [the rename problem](#renaming-a-hook-never-reaches-the-clusters-params)** —
switch it off before measuring stock behaviour.

## Testing a canvas tool without a restart

✅ An installed `.gha` needs a Rhino restart to change, but a build can be *test-loaded*
alongside it. Load it into a fresh, uniquely named `AssemblyLoadContext`; Grasshopper
and RhinoCommon still resolve from the default context, so its types are the live
canvas's types.

```csharp
var alc = new AssemblyLoadContext("MyTool-test-" + n);
var asm = alc.LoadFromAssemblyPath("/path/to/build/MyTool.gha");
asm.GetType("MyTool.Priority").GetMethod("Attach").Invoke(null, null);
AppDomain.CurrentDomain.SetData("mytool.asm", asm);   // reachable from the next run_csharp call
```

To iterate, fetch the previous `Assembly` back from `GetData`, call its `Detach()`,
and load the next build under a new context name. The same assembly name loads
again without complaint.

This needs the plugin to expose **idempotent public static `Attach()` / `Detach()`**
— subscribe and unsubscribe events, install and remove menus, restore any swapped
attributes — with its `GH_AssemblyPriority.PriorityLoad` calling `Attach()`. Put the
test hooks (an interceptor like the one above) on the same static class, so a test
reaches them by reflection off the stashed `Assembly`.

Limits ✅:

- **Nothing is registered with Grasshopper.** The test copy is absent from
  `ComponentServer.Libraries`, and a `GH_Component` inside it gets no proxy
  (`EmitObjectProxy(guid)` is null). So it suits canvas tools and priority-load
  behaviour, **not component libraries**.
- **The load context is not collectible** (`IsCollectible == false`): every loaded
  build stays in the process until Rhino quits. Whatever it did not undo in
  `Detach` stays in effect too.

## Reading Grasshopper's source

Most of this doc came from decompiling. `ilspycmd` (8.2) targets .NET 6; with only
.NET 8 or 10 installed it refuses to start unless told to roll forward:

```bash
export DOTNET_ROLL_FORWARD=Major
RC="/Applications/Rhino 8.app/Contents/Frameworks/RhCore.framework"
ilspycmd -t Grasshopper.Kernel.Special.GH_Cluster \
  "$RC/Versions/A/Resources/ManagedPlugIns/GrasshopperPlugin.rhp/Grasshopper.dll"
ilspycmd -t System.Windows.Forms.ToolStripDropDown \
  "$RC/Resources/System.Windows.Forms.dll"      # the macOS WinForms shim
```

`-t <FullTypeName>` decompiles one type, which is enough for targeted reading. The
shim is the answer to "how does this actually behave on a Mac".

## Related

- [dotnet-build.md](dotnet-build.md) — building the `.gha` in the first place.
- [../write-scripts/rhino-mcp-platform.md](../write-scripts/rhino-mcp-platform.md) —
  the `run_csharp` payload constraints every live test here runs under.
