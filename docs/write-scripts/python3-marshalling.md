# Python 3 Script components: the marshalling flags, and when `set_Text` clears them

`RhinoCodePluginGH.Components.Python3Component` carries three marshalling toggles —
`MarshInputs`, `MarshOutputs`, `MarshGuids` — that decide whether values cross the
Python/Grasshopper boundary as native GH data or as opaque PyObject goo. A component
dropped from the ribbon has all three **true**; that is the constructor default, not
something the UI does afterwards.

**An `IScriptComponent.set_Text` push clears all three to `false` — but only while the
component is *detached*, i.e. not yet in a `GH_Document`.** This is the same class of
side effect as the param-Description reset documented in the `write-csharp-script`
skill: pushing source re-initializes component state.

Re-measured 2026-08-26 on Rhino 8.34.26223.11002 / GH 1.0.0008, macOS, and the
document-membership split is sharp:

| the component when `set_Text` runs | all three flags afterwards |
|---|---|
| detached — `Activator.CreateInstance`, or `EmitObjectProxy(...).CreateInstance()`, before `AddObject` | **`false` / `false` / `false`** |
| already added to a `GH_Document` | **unchanged** — `true` / `true` / `true` survives, on the first push and every later one |

Adding a param (`CreateParameter` + `RegisterInputParam` + `OnParametersChanged` +
`VariableParameterMaintenance`) does **not** clear them on its own; only `set_Text` does,
and only on the detached component. Do not generalise from the detached case — it is the
one a create-then-push pipeline hits, but an in-document push leaves the flags alone.

**This makes Script Forge's capture-and-restore a no-op on 8.34**, because the forge adds
a fresh component to the document *before* it pushes (`if (fresh) d.AddObject(...)`, then
`SyncParams`, then `CaptureMarshalling`, then `set_Text`). Verified end to end: a
component created by a live forge from a header emitting a nine-item list came back with
all three flags `true` and its output unwrapped into nine `GH_Integer`. **Keep the
capture-and-restore** — it costs nothing, it is what makes the ordering safe to change,
and it is the only thing protecting any caller that pushes before adding.

## Why it matters

With `MarshOutputs` off, a Python list assigned to an output param stays wrapped as
**one** PyObject goo (`DataCount = 1`) instead of unwrapping into a GH list. A script
that builds 400 points emits a single opaque item. The source lands intact and the
script computes correctly — only the marshalling configuration is lost, which makes it
an easy failure to misdiagnose as a scripting bug.

## The rule

**Capture the three flags before `set_Text`, restore them after** — and, on a component
you are creating, **add it to the document before you push**. Either one is sufficient on
8.34; doing both is what makes the pipeline robust to the ordering changing. Never force
the flags on unconditionally on an update — the user may have toggled one deliberately.

Setting them at construction time and then pushing source does **not** work, and the split
above is why: a just-constructed component is detached by definition, so the push is
exactly the case that clears them.

**Script Forge already does this**, bracketing its source push with the capture and
restore, so this is background for reading a component rather than a procedure you carry
out. Verified 2026-08-13: a Python 3 component forged from scratch came back with all
three flags true and its outputs marshalled as real GH lists, and they were still true
after a save and reopen. Re-verified 2026-08-26 through a live forge on 8.34, with the
same result — though see the table above for *why* the restore is not what is doing the
work there any more.

## Reading the flags

Grasshopper itself is referenced inside `mcp__rhino__run_csharp`, so the canvas walk is
direct. `Python3Component` is not — it lives in `RhinoCodePluginGH`, which is loaded but
unreferenced (`using RhinoCodePluginGH…` fails to compile) — so the flags themselves come
off by name through reflection:

```csharp
var ghDoc = Grasshopper.Instances.ActiveCanvas?.Document;
foreach (var obj in ghDoc.Objects) {
  var t = obj.GetType();
  if (t.Name != "Python3Component") continue;
  Console.WriteLine($"{t.GetProperty("NickName").GetValue(obj)}: " +
    $"MI={t.GetProperty("MarshInputs").GetValue(obj)} " +
    $"MO={t.GetProperty("MarshOutputs").GetValue(obj)} " +
    $"MG={t.GetProperty("MarshGuids").GetValue(obj)}");
}
```

All three report `CanWrite = true`; the setter works, the value is overwritten later.

## Related: Python output hints are pointless, not destructive

An output hint does **not** break list results by coercing the outgoing value — that
diagnosis sends you after the wrong thing.

Measured 2026-08-25 against all 35 converters, on a Python output emitting a nine-point
list: **no hint collapses anything.** The converter is applied *element-wise* — nine
`GH_Point` became nine `GH_Vector`, nine `GH_Number`, nine `GH_String`, nine `GH_Box`,
all still nine items on one path. Only conversions that are genuinely impossible
(`Curve`, `Guid`, `Point3dList` from a `Point3d`) fail, and they fail loudly, with
*Parameter "a" type conversion failed from Point3d to Curve* and an empty output.

The collapse is `MarshOutputs`, exactly as described above, and the hint is a red herring:

| `MarshOutputs` | output hint | result |
|---|---|---|
| `true` | any convertible hint, or none | **9 items**, unwrapped |
| `false` | `object` / no hint | **1** `GH_ObjectWrapper` holding the whole list |
| `false` | `Point3d` | **empty** |

The bottom row is what makes the hint look guilty. A push onto a *detached* component
clears the flags, so a create-then-push onto a component whose output happened to carry a
hint produces an *empty* output rather than the merely-collapsed one — a worse symptom,
with the hint as the visible difference between the two components. Restore the flags and
the hint is inert.

**The guidance is unchanged: leave Python outputs unhinted, and treat an output's header
`type` as documentation.** It matches stock components, and an output hint buys nothing in
either language. But if a Python list output is coming back collapsed or empty, check
`MarshOutputs` — removing the hint will not fix it.

## Otherwise identical to `CSharpComponent`

`Python3Component` exposes the same `ScriptVariableParam` params, the same type-hint
converters, and the same `set_Text`, so every reflection recipe in this kit works on
both. One quirk: a `double` hint reads back as `TypeName` `"float"`.
