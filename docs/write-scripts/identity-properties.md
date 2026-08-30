# Name, NickName and Description on script components and their parameters

Where a script component's identity actually lives, which slot is archived into the `.gh`,
and which of the terms this kit uses are Grasshopper's and which are ours.

All measurements: Rhino 8.33 / Grasshopper 8.33.26188.13002, macOS, 2026-08-05. "Both
surfaces" below means a **script** component (`RhinoCodePluginGH`) and a **compiled** one.

> **This is reference, not a procedure.** Script Forge writes both durable slots on every
> pass, so nothing here is a step you carry out — it is the explanation of *why* the
> durable slot differs from the volatile one, and what to read when a tooltip comes back
> wrong. Stamping is `forge-push`'s job; see [script-forge.md](../use-the-forge/script-forge.md).

## The three properties every parameter has, on both surfaces

`Name`, `NickName`, `Description` — all inherited from `GH_InstanceDescription`.

**Grasshopper's rules for them are identical on both surfaces:**

| what you see                 | comes from                                                | source                                                                              |
| ---------------------------- | --------------------------------------------------------- | ----------------------------------------------------------------------------------- |
| the label drawn on the param | `NickName`                                                | `GH_ComponentAttributes.RenderComponentParameters` → `DrawString(item.NickName, …)` |
| tooltip **title**            | `Name` if `Name == NickName`, otherwise `Name (NickName)` | `GH_Attributes<T>.SetupTooltip`                                                     |
| tooltip **body**             | `Description`                                             | same method, `e.Text = Owner.Description`                                           |

So `Name (NickName)` isn't a script-component feature — it's what Grasshopper does anywhere those two properties disagree.

### "Draw Full Names" is a placement-time overwrite, not a render-time toggle

Grasshopper draws `NickName` on the param unconditionally — `RenderComponentParameters` reads
no other name property and consults no setting. The Display ▸ **Draw Full Names** menu item works
a different way entirely: `CentralSettings.CanvasFullNames` has exactly seven call sites in
`Grasshopper.dll`, and the only one on the placement path is `GH_Canvas.InstantiateNewObject`,
where it walks the new object's whole attribute tree and assigns **`NickName = Name`** on the
component *and every parameter*, once.

Three consequences:

- It only affects objects placed *while the setting is on*. Existing components are untouched, and
  turning it back off does not restore the short nicknames — they were overwritten in the document.
- It is destructive on a **script** component, where `NickName` is the C# identifier: a param with
  `Name = Keys` / `NickName = OutKeys` would be silently renamed to `Keys`, colliding with an input
  of that name and desynchronising the params from the `RunScript` signature.
- **Anything that adds components with `GH_Document.AddObject` is immune** — this kit's
  reconstruction path and Script Forge both do, so the stamp never runs on a component they
  create. Switching to `GH_Canvas.InstantiateNewObject` (for undo support, say) inherits the
  bug; take `GH_UndoUtil.RecordAddObjectEvent` on its own instead.

## Extra properties only a script component's parameter has

Type is `RhinoCodePluginGH.Parameters.ScriptVariableParam`. All four are **aliases onto the
three real properties** — no independent storage — but three of them are read *and* **write**,
not read-only:

| property        | accessors  | what it actually is                                                                                                             |
| --------------- | ---------- | -------------------------------------------------------------------------------------------------------------------------------- |
| `VariableName`  | get + set  | the same storage as `NickName`. This is the C# identifier.                                                                      |
| `PrettyName`    | get + set  | the same storage as `Name`.                                                                                                     |
| `HasPrettyName` | **get only** | `Name != NickName`                                                                                                            |
| `ToolTip`       | get + set  | backs `_descriptionOverride`; writing it writes `Description` too. When blank, `UpdateFromConverter()` overwrites `Description` with generic converter text |

So each of the three real properties has exactly one script-side partner:

| real property | script-side partner | relationship                                        |
| ------------- | ------------------- | ---------------------------------------------------- |
| `Name`        | `PrettyName`        | interchangeable — same storage, either direction     |
| `NickName`    | `VariableName`      | interchangeable — same storage, either direction     |
| `Description` | `ToolTip`           | **not** interchangeable — see below                  |

The first two pairs are symmetric: setting either half is the same operation. A bare
`ScriptVariableParam`, poked one property at a time:

| after setting          | `Name`     | `NickName`  | `VariableName` | `PrettyName` | `HasPrettyName` |
| ---------------------- | ---------- | ----------- | -------------- | ------------ | --------------- |
| *(fresh)*              | var        | var         | var            | var          | False           |
| `Name = "Keys"`        | **Keys**   | var         | var            | **Keys**     | True            |
| `NickName = "OutKeys"` | Keys       | **OutKeys** | **OutKeys**    | Keys         | True            |
| `VariableName = "Renamed"` | Keys   | **Renamed** | **Renamed**    | Keys         | True            |
| `PrettyName = "Pretty"` | **Pretty** | Renamed    | Renamed        | **Pretty**   | True            |

So prefer whichever name states the intent: `VariableName` when you mean the C# identifier,
`NickName` when you mean what Grasshopper draws.

**The third pair is one-way, and that asymmetry is the whole story of the tooltip.** `ToolTip` is
the override slot; `Description` is the rendered value:

| after setting                      | `ToolTip`  | `Description` | survives a reopen |
| ---------------------------------- | ---------- | ------------- | ----------------- |
| *(fresh)*                          | *(blank)*  | No conversion | —                 |
| `ToolTip = "my tooltip"`           | my tooltip | **my tooltip** | **yes**          |
| `Description = "my tooltip"`       | *(blank)*  | my tooltip    | **no** — reverts to converter text |

Writing `ToolTip` writes `Description` as well, so there is never a reason to write both. Writing
`Description` alone leaves `ToolTip` blank, and a blank `ToolTip` is exactly the condition under
which `UpdateFromConverter()` replaces `Description` with the converter's generic text. Note also
that the generic text is present from birth (`"No conversion"` on a fresh param), not applied
later as a fallback.

Beware the spelling: the parameter's is `ToolTip`, capital T. The script *component* has its own
`Tooltip`, lowercase, declared on `BaseScriptComponent<,>` — a different property on a different
object, and it behaves differently again (see below).

**The crux:** on a script component `NickName` does two jobs at once — it is what Grasshopper
draws on the param, *and* it is the C# identifier. A compiled component's `NickName` is only a
label.

**But the property setter does not validate.** `VariableName = "Not An Identifier"` is accepted
silently, spaces and all; nothing throws and nothing sanitizes. Rhino's identifier validation
lives in the right-click parameter editor UI, not on the property — so anything writing these by
reflection (Script Forge, the kit's stamping skills, `gh_meta.py --check`) has to validate the
identifier itself. A bad one surfaces as a compile error in the script, far from its cause.

`Name` is free-form on both. Rhino's own right-click menu on a script parameter spells this out: the top textbox edits `VariableName`, and a separate box is labelled **"Name (for humans, optional):"**, described as *"This is .Name property"*. A third box, **"Tooltip (optional):"**, is described as *".Description property"*.

**That third label is wrong, and usefully so.** Typing into the Tooltip box writes `ToolTip`, not
`Description` — verified by filling it in by hand and reading both properties back. Which is why
tooltips typed by a human have always survived a reopen while stamped ones evaporated: the UI was
writing the durable slot and our tooling was not.

## Component level (not parameters)

Same three properties, `Name` / `NickName` / `Description`, on the component object — plus a
fourth that is easy to miss: **`Tooltip`** (lowercase t), declared on `BaseScriptComponent<,>`.

There are therefore *three* ways to set a script component's description, and they do three
different things:

| write                            | holds through a solve | survives a save/reopen |
| -------------------------------- | --------------------- | ---------------------- |
| `GH_InstanceDescription.Description` | no — regenerated   | no                     |
| `IScriptComponent.set_Description`   | yes                | **no**                 |
| `BaseScriptComponent.Tooltip`        | *doesn't populate Description at all in-session* | **yes — and it overwrites `Description` on load** |

Measured by a save-and-reopen round trip: a component with `Tooltip = "ROUND2-TOOLTIP"` and
`Description = "ROUND2-DESCRIPTION"` (set through the script interface) came back with **both**
reading `ROUND2-TOOLTIP`. So `set_Description` is what makes the tooltip correct *now*, and
`Tooltip` is what makes it correct *next time* — and a stale `Tooltip` will silently clobber a
freshly stamped description on the next open. Write both.

Unlike the parameter's `ToolTip`, the component's `Tooltip` does **not** feed `Description` within
the session; it is applied only on load. That asymmetry is the trap.

A compiled component takes all three as constructor arguments and they never move.

## Which of these terms are ours, not Grasshopper's

None of them, any more. The `@component` header's two per-param naming keys are named after the
properties they reach:

| header key on a param object | where it lands, script component | where it lands, compiled               |
| ---------------------------- | -------------------------------- | -------------------------------------- |
| `variableName`               | `NickName` (== `VariableName`)   | *nowhere* — it only becomes a C# local |
| `name`                       | `Name` (== `PrettyName`)         | `Name` **and** `NickName`              |

`variableName` defaults to `name` when omitted, which is the common case: one key, both slots.
There are three real properties — `Name`, `NickName`, `Description` — and two aliases worth
knowing, `VariableName == NickName` and `PrettyName == Name`. Terms like "code name" or
"pretty label" appear in no Grasshopper API; prefer the real names.

### The vocabulary, so it stops drifting

Four senses get confused with each other, and "pin" — borrowed from node-editor vernacular —
is the usual culprit. Grasshopper has no word for the circle on a component's side because
the circle *is* the parameter; say what the sentence means instead.

| sense | say | not |
| ----- | --- | --- |
| the `IGH_Param` / `ScriptVariableParam` object | **param** — or **input** / **output** wherever the side is known | pin, socket |
| a JSON entry in the header's `inputs[]` / `outputs[]` | **param object** | |
| a standalone component off the Params ribbon tab | **param component** | |
| the string Grasshopper draws on it | **`NickName`**, or "what Grasshopper draws on the param" | pin label, canvas label, param name, param nickname |
| the C# argument / Python global bound to it | **the identifier** — `variableName` for the header key | code name |

Each rejected term collides with something this doc already distinguishes: **param name** is
`Name`, which is *not* the drawn one; **param nickname** reads as the header's `nickname` key,
which only a compiled build ever draws; **component label** is the component's own `NickName`
(see "Component level" above); **canvas label** names the whole workspace.

**"Label" stays ordinary English** — "identically-labelled params", the forge's `LABEL WARNING` —
and never becomes a defined term, because a script param has *two* label slots (`Name` and
`NickName`) and a bare "the label" names neither. The verb "pin" is untouched: pinning a source
to an `instanceGuid` is unrelated to any of this.

## What survives a save, quit, and reopen

Measured 2026-08-05 on a live canvas: a forged script component was given two different
descriptions — one written to `Description` with `ToolTip` left blank, one written to `ToolTip` —
then saved, Rhino was quit and relaunched, and the file reopened.

| property                            | before                      | after reopen                              |
| ----------------------------------- | --------------------------- | ----------------------------------------- |
| component `Name` / `NickName`       | Name Demo / NameDemo        | **survived**                              |
| component `Description`, `Tooltip` blank | header text            | **empty**                                 |
| component `Tooltip`                 | sentinel string             | **survived**, and overwrote `Description` |
| param `NickName`                    | OutKeys                     | **survived**                              |
| param `Name` (diverged from NickName) | Keys                      | **survived** — `HasPrettyName` still True |
| param `ToolTip`                     | sentinel string             | **survived**                              |
| param `Description`, `ToolTip` set  | sentinel string             | **survived**                              |
| param `Description`, `ToolTip` blank | sentinel string            | **lost** → `Converts to collection of text fragments` |

Two things follow, and they matter more than they look.

**`Name` is durable and independent of `NickName`.** A param can come back with
`Name = Keys` / `NickName = OutKeys` and its tooltip title still reading `Keys (OutKeys)`. The
display-name divergence is not a session-only decoration — it is archived. Both slots survive a
save and reopen, so a header `name` needs no restamping.

**`ToolTip` is the durable slot for a description; `Description` alone is not.** `ToolTip` backs
`_descriptionOverride` and is archived; when it is blank, `UpdateFromConverter()` overwrites
`Description` with the converter's generic text on load.

### The same slot also survives a `set_Text` push

The standing rule that a source push resets every param description holds only for params whose
`ToolTip` is empty. Measured with a single push of genuinely changed source, one param on each
storage path:

| param | before the push | after the push |
| ----- | --------------- | -------------- |
| `ToolTip` cleared, `Description` set by hand | `DESC-ONLY: no ToolTip backing this one` | **wiped** → `Converts to collection of text fragments` |
| `ToolTip` set | header text | **unchanged** |

So writing `ToolTip` does not merely survive a reopen — it also means a param needs no
restamping after a push. (Push identical source and nothing is regenerated at all, so a
meaningful test must change the text.)

Script Forge writes `ToolTip` where the param has one, falling
back to `Description` only for a param class that has none — that is its `StampDescriptions`,
and since the forge is the only stamping path in this kit there is nothing else to keep in
step. Verified end to end: a
component was pushed, stamped and round-tripped through save/reopen with every param tooltip
intact.

The fallback is *not* what handles the built-in `out` print param: no header can declare `out`,
so it is skipped at the name match and keeps its stock description.

Descriptions coming back empty after a save and reopen is **not** evidence that they are
unserialized — both the component's and every param's are archived. That symptom is the
load-time regeneration described above, driven by a durable slot left blank, and filling the
slot is the fix.

## Reaching the durable slots by reflection

Neither slot is a plain flattened `GetProperty` away — the component's is declared on a generic
base, and a derived type may shadow either name. Walk the `BaseType` chain with `DeclaredOnly`
and let the caller fall back when the member is absent:

```csharp
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
```

`DeclaredOnly` plus the walk covers a slot declared non-publicly on a base — `GetProperty` does
not return those — and cannot throw `AmbiguousMatchException` on a shadowed name.

Reflection is required here, not a stylistic choice: `RhinoCodePluginGH` is loaded but **not
referenced** by a `run_csharp` payload, so `using RhinoCodePluginGH.Parameters;` fails to
compile (`CS0246`) even though the assembly is right there. Verified 2026-08-13. Script Forge
carries its own copy of this walk for the same reason.

## How the claims here were checked

Reflection against the running Rhino 8.33 / Grasshopper 8.33.26188.13002, 2026-08-05:

- **accessors** — `PropertyInfo.GetSetMethod(true) != null` on each member of `ScriptVariableParam`.
- **alias behaviour** — a bare `ScriptVariableParam` instantiated and poked one property at a time,
  reading all five back after each write (the table above).
- **`RenderComponentParameters`** — IL scan of the method body for name accessors; the only one
  called is `IGH_InstanceDescription.get_NickName`.
- **`CanvasFullNames`** — IL scan of every method in `Grasshopper.dll` for a call to its getter,
  then a read of the one call site on the placement path.
- **persistence** — first a real save / quit Rhino / relaunch / File ▸ Open round trip, then the
  cheaper in-process equivalent below. Sentinel strings distinguished the storage paths.
- **the UI's behaviour** — the right-click parameter editor's boxes filled in by hand, then both
  properties read back.

### Testing persistence without restarting Rhino

`GH_DocumentIO` round-trips a document through the real serializer in-process, which reproduces
the load-time behaviour (`UpdateFromConverter`, the `Tooltip` → `Description` restore) without a
restart:

```csharp
var io1 = new GH_DocumentIO(doc);  io1.SaveQuiet(tmpPath);
var io2 = new GH_DocumentIO();     io2.Open(tmpPath);
// io2.Document holds a freshly deserialized copy — inspect it, don't add it to the canvas
```

Reached by reflection from the script sandbox, since `GH_IO` and `Grasshopper` are not referenced
there. Note `SaveAs()` takes no arguments — `SaveQuiet(path)` is the one that writes to a path.

### A methodology warning

Scanning IL for a member's `MetadataToken` as raw bytes and concluding "zero call sites" means dead
code is unsafe inside a **generic** type — `BaseScriptComponent<,>` here. References to the type's
own members are emitted as MemberRef/TypeSpec tokens, not the MethodDef/FieldDef token the scan
matches, so a live member reads as unreferenced. `Tooltip` is the component's durable description
slot despite reading as dead on such a scan. Token scans are safe on non-generic types; anywhere
else, confirm with a behavioural test.