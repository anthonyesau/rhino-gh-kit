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

**A document built in code never byte-matches its own copy** ✅ — until it has been
written and read back once. An in-memory `GH_Document` serializes its `Name` as `""`
where every read-back copy says `"unnamed"`, and attributes never laid out keep their
default bounds (150 × 20) where a read-back copy's are laid out. A cluster made with
`CreateFromDocument(inMemoryDoc)` therefore never matches an entangled copy of itself.
The first round trip is the only one that changes anything; after it, every further
copy is byte-identical. Tools and test fixtures that assemble cluster contents in code
should round-trip the document once before `CreateFromDocument`:

```csharp
var chunk = new GH_LooseChunk("Document");
built.Write(chunk);
var read = new GH_LooseChunk("Document");
read.Deserialize_Binary(chunk.Serialize_Binary());
var doc = new GH_Document();
doc.Read(read);          // use this, not `built`
```

A cluster made on the canvas (select ▸ Cluster) and then copied does byte-match — the
problem is specific to documents assembled in code.

### A cluster commit raises no event of its own

✅ The only signal is the undo record `DocumentModified` pushes after rebuilding the
family, named **`"Cluster Change"`**. Subscribe to `GH_Document.UndoStateChanged` on
every server document — cluster-editor documents included — and filter on
`e.Operation == GH_UndoOperation.RecordAdded` and `e.Record.Name == "Cluster Change"`.

The record holds one `GH_GenericObjectAction` per rebuilt cluster. Its target is the
private field `m_object_id` on `GH_GenericObjectAction`; resolve it with
`doc.FindObject(id, false)`. Most other object actions — persistent data, nickname
changes — derive from `GH_ObjectUndoAction`, which has its **own, separate** private
`Guid m_object_id` ✅. Walk the action's type hierarchy for a `Guid` field of that name
to cover both. `FindObject(id, topLevelOnly: false)` resolves a component's **param**
too ✅, so a record leads to the exact input it changed.

`UndoStateChanged` for `Undo` / `Redo` fires **after** the record has been applied ✅
(`GH_UndoServer.PerformUndo`), so a handler sees the restored state. Undo restores into
the **same `GH_Cluster` instance** ✅ — `FindObject` returns the same reference — but
reads its whole inner document back, so **clusters nested inside it are new objects**
after an undo or redo. Re-fetch a nested cluster from the parent's inner document
rather than holding a reference across one.

Record counts and names are on `doc.UndoServer` (`UndoCount`, `FirstUndoName`,
`UndoNames`), not on `doc.UndoUtil`.

### The undo action holds the other side of the commit

`GH_GenericObjectAction` derives from `GH_ArchivedUndoAction`, whose protected
`byte[] m_data` is the object serialized into a `GH_LooseChunk("data")` 📖. Each undo
and redo swaps it with the live state (`Internal_Redo` just calls `Internal_Undo`), so
**after any operation it holds the state from the other side of it**: the "before"
after a commit or a redo, the "after" after an undo. A detached copy reads back ✅:

```csharp
var mData = typeof(GH_ArchivedUndoAction).GetField("m_data", BindingFlags.NonPublic | BindingFlags.Instance);
var chunk = new GH_LooseChunk("data");
chunk.Deserialize_Binary((byte[])mData.GetValue(action));
var before = new GH_Cluster(); before.CreateAttributes();
before.Read(chunk);   // in no document, with its full inner document
```

This is how to tell what a commit actually changed — for instance, which nested
clusters differ between "before" and "after" by `DocumentId` and serialized bytes.
That comparison is stable between a read-back "before" and a freshly committed "after"
✅.

### A cluster's inputs: their type, and matching them across copies

`GH_Cluster.CreateInput(hook)` 📖 makes each new input param as:

- a duplicate of the hook's **single recipient's** param type — a `Param_Number` when
  the hook feeds a Number param;
- with several recipients, the type they share, emitted by the component server;
- otherwise a `Param_GenericObject`;

then sets `Optional = true` and a new `InstanceGuid`. So a cluster input can hold
persistent data ("defaults") exactly when that type can, and the type is decided inside
the cluster.

Entangled copies are read from the same serialized contents, so **their hooks share
`InstanceGuid`s** ✅. `m_mapping.get_Hook(paramGuid)` ([above](#renaming-a-hook-never-reaches-the-clusters-params))
gives the hook id, and that id finds the same input on another copy even after params
were reordered.

### Which file a document belongs to

A cluster editor's document has `Owner` = the cluster (see
[below](#grasshoppers-own-ui-handlers-can-be-driven-from-code)), and so does a
cluster's **inner** document 📖 (`Owner = this`, `Nested = true`). A cluster nested in
another answers `OnPingDocument()` with the **inner** document ✅ — whichever document
it was added to. So one loop finds the top-level document — the file — for any
document: a top-level one, an editor, an editor opened inside an editor, or an inner
document ✅:

```csharp
static GH_Document FileOf(GH_Document doc)
{
    for (int i = 0; doc != null && i < 64; i++)
    {
        if (!(doc.Owner is GH_Cluster owner)) return doc;
        var parent = owner.OnPingDocument();
        if (parent == null || ReferenceEquals(parent, doc)) return doc;
        doc = parent;
    }
    return doc;
}
```

Any "only in this file" scope needs it: editing a nested cluster from inside its
parent's editor commits in the *editor* document, so treating editors as separate files
cuts the main document off.

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

## Params and persistent data

### `SolutionExpired` is raised on the top-level object only

`GH_DocumentObject.OnSolutionExpired` 📖 raises the event on the object itself only
while its attributes are top-level; otherwise it forwards to
`Attributes.GetTopLevel.DocObject.OnSolutionExpired`. So **a component's input or output
param never raises `SolutionExpired`** ✅ — a listener on `cluster.Params.Input[0]` never
fires, however often the param expires. Subscribe to the owning component and check
which param changed.

### Setting persistent data: the undo record comes first

Every way of setting a param's persistent data 📖 — `SetPersistentData` (all
overloads), Set one / Set multiple, Manage collection, Clear values, the multiline
editor, Internalise data — records the undo event **first**, then changes
`PersistentData`, then raises `OnObjectChanged` and expires. So an `UndoStateChanged`
handler that sees `RecordAdded` for such a record still reads the **old** value. The
reliable after-change signal is the owning component's next `SolutionExpired`.

For **Undo and Redo** it is the other way round: `GH_PersistentDataAction` restores the
data before `UndoStateChanged` reports the operation, so a handler can act at once.

**Pattern** ✅ — on `RecordAdded`, arm a one-shot `SolutionExpired` handler on the owning
component that unsubscribes on its first fire. Keep armed handlers in a dictionary so
`Detach` can remove any that never fired. On Undo and Redo, act immediately.

Two traps 📖✅:

- **`SetPersistentData(T)` and `SetPersistentData(IEnumerable<T>)` append** to the
  existing data (as does `params object[]`, which forwards to the list overload). Only
  `SetPersistentData(GH_Structure<T>)` replaces it.
- **Clearing `PersistentData` yourself before `SetPersistentData(item)`** puts the clear
  *before* the undo snapshot, so undo restores the cleared state, not the old value. To
  replace a value with working undo, build a `GH_Structure<T>` and use that overload.

### Small traps

- **`GH_Document.AddObject` marks the document modified** ✅, so a test cannot assert
  "not modified" on a fixture it just populated.
- **`GH_SettingsServer.ConstainsEntry(string)`** is Grasshopper's spelling; there is no
  `ContainsKey`. `DeleteValue(key)` restores "absent" ✅.

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

### Menu item tooltips never show on macOS

✅ On Rhino 8 for Mac, hovering over a Grasshopper menu item shows nothing, even when
`ToolStripItem.ToolTipText` is set and reads back from the item. This happens on
items added to the editor's main menu, on items added to an object's right-click
menu (`GH_CanvasMenuStrip`), and on Grasshopper's own items. Both menus are native
`NSMenu`s on macOS, and the tooltip does not reach them. Whether the same tooltips
show on Windows has not been checked.

- **Put nothing a user needs into a tooltip.** A Mac user never sees it.
- **Put anything essential in the item's text.** A count goes in the name ("Refresh 3
  Labels"), not in the tooltip.
- **Name each item so it makes sense with no explanation.** For a Mac user, the only
  other place to find out what it does is the plugin's own docs.
- Setting the tooltip anyway costs nothing, and it may give Windows users extra detail.

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
- **`cluster.DocumentModified(editedDoc)`** commits without opening the editor ✅, where
  `editedDoc = GH_Document.DuplicateDocument(<m_internalDocument>)`, then edited. It
  rebuilds the family in the owning document and pushes `"Cluster Change"`
  (`UndoStateChanged`: `ClearRedoStack`, then `RecordAdded`). It is what Save & Close
  calls, minus the editor, and needs no canvas; the file-rewrite caveat above applies.
- **A right-click without a mouse** ✅: call
  `attrs.RespondToMouseUp(canvas, new GH_CanvasMouseEvent(controlPoint, canvasPoint, MouseButtons.Right, 1, 0))`
  at the centre of the object's own `Bounds` — that constructor needs no viewport. Run
  `ExpireLayout()` and `PerformLayout()` first or `Bounds` may be stale. With a menu
  interceptor ([above](#menus-and-dialogs-block-the-call-that-opens-them)) it returns
  the built `ToolStripDropDown`, and `item.PerformClick()` runs the real handler. Set
  `canvas.Document` to the object's document for the duration (handlers check
  `sender.IsDocument`) and restore it after.
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
- **`new AssemblyLoadContext(name)` is not collectible**: every build loaded that way
  stays in the process until Rhino quits. Pass `isCollectible: true` instead and
  `Unload()` it after `Detach()` ✅: the context then disappears from
  `AssemblyLoadContext.All`. That is proven only for builds that detach everything
  they attached. Handlers or attributes left on live objects keep the context alive,
  and whether unloading is then safe or merely leaks is untested.
- Whatever a build did not undo in `Detach` stays in effect.

**A smoke run of the Release build** ✅ follows the same shape: detach the installed
copy, load the build into a collectible context, `Attach`, drive scratch documents,
then `Detach`, restore any settings, `Unload`, and re-attach the installed copy. A test
suite built as its own assembly is the next step up — see
[testing-in-grasshopper.md](testing-in-grasshopper.md).

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
- [testing-in-grasshopper.md](testing-in-grasshopper.md) — a test suite that exercises
  all of this from a separately built assembly.
- [../write-scripts/rhino-mcp-platform.md](../write-scripts/rhino-mcp-platform.md) —
  the `run_csharp` payload constraints every live test here runs under.
