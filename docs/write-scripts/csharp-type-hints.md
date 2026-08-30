# C# Script param type hints (the `type` vocabulary)

A Rhino 8 **C# Script** component (`CSharpComponent`, `b6ba1144-…`) types each input
through a per-param **Converter** (right-click → Type Hint). The `RunScript` body
signature and that Converter are kept in sync in *both* directions, on different
triggers — see "Which way the sync runs" below — but only the Converter is durable: GH
rewrites the signature from the params on every solve. Every input param is a `RhinoCodePluginGH.Parameters.ScriptVariableParam`
carrying a `ScriptVariableTypeHintSet`; the selected converter is what actually
types the incoming data.

This is the single authoritative list of the `type` strings that select those
converters. One thing sets a hint in this kit — **Script Forge**, from the
`@component` header's `type` on each param — and this is the vocabulary it resolves
against.

## Which way the sync runs

Three triggers, and conflating them is the usual source of confusion. Verified
2026-08-25 on Rhino 8.34, macOS:

| trigger | direction |
|---|---|
| **ScriptEditor save** — a human pastes source and closes the editor | **params ← signature.** The editor creates, names, orders and *hints* the params from the `RunScript` argument list. `RunScript(GeometryBase Geometry, double Radius, int Count, double Height, out object Points, out object Arrayed)` pasted into a fresh component yields six correctly named params with `GeometryBase` / `double` / `int` / `double` selected, and it solves. The sync is `RhinoCodeEditor.Editor.RCEState+StateCode.SetCodeParamsFromServers()` — it lives in the **editor**, driven by language servers, not on the component. |
| **solve** | **signature ← params.** Every argument is regenerated from its param's hint; an unhinted param becomes a plain `object`, whatever the file said. |
| **programmatic `set_Text`** | **neither.** The text lands, the params do not move, and the next solve rewrites the signature from them. |

Two consequences worth holding onto:

- **A typed signature is not decorative.** It is what a person pasting a `.cs` into the
  editor gets their params from, and it is the only declarative route a script component
  has. What it does *not* carry is any documentation: per-param `ToolTip` stays empty
  (the `Description` shows the converter's generic text) and the component's `Name`,
  `Description` and icon are untouched.
- **`set_Text` is not equivalent to an editor save.** Script Forge, `run_csharp` payloads
  and any other reflection-driven push take the third row, which is why the forge stamps
  every param itself. A test built on `set_Text` will conclude that pasting configures
  nothing — true of that path, false of the one a person uses.

## Reading a hint back

**A script param's own `TypeName` always reads `"Generic Data"`**, whatever hint is
selected — it describes the param class, not the converter. It is not the thing to check.
The hint itself lives one level down:

```csharp
((ScriptVariableParam)param).TypeHints.GetSelected().TypeName
```

That is the only read that answers "which converter is on this param". Anything else
reporting `Generic Data` is describing the param, not reporting a failure to apply.

**A `type` that names no converter falls back to `No Type Hint`** — the param stays
generic and the component still solves, so a typo (`"Intrval"`, `"pt"`, `"Curv3"`) does
not fail loudly. The forge does report it, as `HINT WARNING: <param> — unknown hint
<type>, using No Type Hint` in its `Log`, which is a good reason to actually read the Log
rather than trust `Success`. Spell names from the table below verbatim.

Converter names are **language-specific**, which is why the forge resolves through a
candidate table rather than passing the header string straight through: the double
converter is named `float` on a Python 3 component, and `string` is `str`. Write the C#
spelling in the header either way.

## The signature as an oracle

The **solve** row of the table above is usable in reverse, and it is the cheapest way to
learn Grasshopper's own canonical spelling for any param state rather than guessing at it.
Because every solve regenerates the `RunScript` argument list *from the params*, you can
set param state imperatively, solve, and read `IScriptComponent.Text` back — whatever
Grasshopper wrote is, by definition, the spelling it accepts.

```csharp
var iface = comp.GetType().GetInterfaces().First(i => i.Name == "IScriptComponent");
var text  = (string)iface.GetProperty("Text").GetValue(comp);   // signature included
```

This works on **C# and SDK-mode Python alike** — script-mode Python has no signature and
is never rewritten. It is how the Python annotation vocabulary in
[python3-marshalling.md](python3-marshalling.md) was established: 35 converters set one at
a time, each signature read back, no guessing. It also settles access, which has no
obvious spelling: item is `Name: T`, list is `Name: list[T]`, tree is
`Name: Grasshopper.DataTree[T]`, and an `object`-hinted or unhinted param comes back as a
bare `Name` with no annotation at all.

Two things will mislead you if you do not know them:

- **The readback lags one solve.** `Text` serves a cached copy, and extra
  `ExpireSolution(true)` calls do **not** flush it — the value you read reflects the
  *previous* param state while already showing the current hint. Either decode by shifting
  the results one row, or read the text in a separate `run_csharp` call after the solve has
  settled. A run that looks like it produced nonsense (`Point3d` + item access reported as
  `DataTree[Point3d]`) is almost always this.
- **Only shape changes trigger a rewrite.** Changing `Optional`, or writing persistent
  data, regenerates nothing. That is not a bug to work around — it is the finding: state
  that never appears in a regenerated signature is state a signature cannot declare. It is
  how "Python defaults are not declarable" was established, and the same reasoning applies
  to anything else you are tempted to put in a signature.

## The vocabulary

Matching is **case-insensitive** and accepts both the converter's own `TypeName` and
a few common lowercase aliases (`point`→`Point3d`, `vector`→`Vector3d`, etc.).
Verified on Rhino 8, macOS.

| `type` string (+ aliases) | Selected converter | Incoming CLR type |
|---|---|---|
| `bool` | BooleanConverter | `bool` |
| `int` | IntConverter | `int` |
| `double` | DoubleConverter | `double` |
| `string` | StringConverter | `string` |
| `Complex` | ComplexConverter | `Complex` |
| `DateTime` | DateTimeConverter | `DateTime` |
| `Color` / `color` | ColorConverter | `System.Drawing.Color` |
| `Point3d` / `point` | Point3dConverter | `Point3d` |
| `Point3dList` | Point3dListConverter | `Point3dList` |
| `Vector3d` / `vector` | Vector3dConverter | `Vector3d` |
| `Plane` / `plane` | PlaneConverter | `Plane` |
| `Interval` | IntervalConverter | `Interval` (a GH **Domain**) |
| `UVInterval` | UVIntervalConverter | `UVInterval` (a GH **Domain²**) |
| `Guid` | GuidConverter | `Guid` |
| `Box` | BoxConverter | `Box` |
| `Transform` | TransformConverter | `Transform` |
| `Line` | LineConverter | `Line` |
| `Circle` | CircleConverter | `Circle` |
| `Arc` | ArcConverter | `Arc` |
| `Curve` / `curve` | CurveConverter | `Curve` |
| `Polyline` | PolylineConverter | `Polyline` |
| `Rectangle3d` | Rectangle3dConverter | `Rectangle3d` |
| `Mesh` / `mesh` | MeshConverter | `Mesh` |
| `Surface` | SurfaceConverter | `Surface` |
| `Extrusion` | ExtrusionConverter | `Extrusion` |
| `SubD` | SubDConverter | `SubD` |
| `Brep` / `brep` | BrepConverter | `Brep` |
| `PointCloud` | PointCloudConverter | `PointCloud` |
| `GeometryBase` | GeometryBaseConverter | `GeometryBase` (any geometry) |
| `Hatch` | HatchConverter | `Hatch` |
| `TextDot` | TextDotConverter | `TextDot` |
| `TextEntity` | TextEntityConverter | `TextEntity` |
| `Leader` | LeaderConverter | `Leader` |
| `No Type Hint` / `object` / *(anything unrecognized)* | GooConverter | **the goo's `ScriptVariable()`** — see below |

⚠️ **"No Type Hint" does not mean "you get the goo".** It means *no type* is imposed —
the goo is still unwrapped to its `ScriptVariable()`. ✅ Measured on Rhino 8.33: a Panel
feeding an `object` param delivers `System.String`, not `GH_String`; a `GH_ObjectWrapper`
around an `IGH_DocumentObject` delivers the document object itself, not the wrapper. So
an `object`-hinted param is "give me the underlying value, whatever it is" — not "give me
the wire's goo".

This matters most when compiling a suite into a `.gha` (`docs/ship-a-plugin/dotnet-build.md`), where
you write the marshalling yourself and must reproduce the unwrap. Code that defensively
tests `is GH_ObjectWrapper` or `is IGH_Goo` on such an input is belt-and-braces whose
branches never fire — do not read it as evidence that goo arrives.

There is also a second `FilePathConverter` whose `TypeName` is `string`; the plain
`string` name selects the ordinary `StringConverter`. Reach the file-path variant via
the UI if you specifically need it.

`Access` is orthogonal to `type`: `item` (single value), `list` (one branch), `tree`
(multi-branch). Access mismatches produce runtime warnings, not compile errors.

## Enumerating the live list yourself

The set is defined by the installed ScriptEditor plugin and could grow across Rhino
versions. To dump the current machine's list, iterate a live param's hint set. Send this
through `mcp__rhino__run_csharp`, which compiles against Grasshopper and prints to stdout
(the hint set itself still needs reflection — `ScriptVariableParam` lives in
`RhinoCodePluginGH`, which is not referenced):

```csharp
// input0 is a ScriptVariableParam on any C# Script component
var hints = input0.GetType().GetProperty("TypeHints").GetValue(input0);
foreach (var h in (System.Collections.IEnumerable)hints)
    Console.WriteLine((string)h.GetType().GetProperty("TypeName").GetValue(h));
// Select one:  hints.GetType().GetMethod("Select", new[]{typeof(string)}).Invoke(hints, new object[]{ "Interval" });
// Read current: hints.GetType().GetMethod("GetSelected").Invoke(hints, null)  // → converter, .TypeName is the string
```
