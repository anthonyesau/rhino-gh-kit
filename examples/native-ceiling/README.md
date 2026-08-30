# What a script component can set about itself

No Script Forge, no compiled plugin, no MCP — nothing but stock Rhino 8. Four files:
one component, **Ring Array**, written two ways in both languages, so the cost of
each approach is visible side by side.

| | what it does | where the source lives | C# | Python 3 |
|---|---|---|---|---|
| **Stock** | logic only — the `RunScript` signature declares what it can, you build the rest by hand | pasted in | `ring-array-stock.cs` | `ring-array-stock.py` |
| **Auto-configured** | the script builds its own params and metadata | pasted in **or** read off disk | `ring-array-auto-configured.cs` | `ring-array-auto-configured.py` |

There is no third, "linked" version: hardening a script for the `script` param turns out
to be nothing but *correct param addressing*, which the auto-configured version needs
anyway. What linking really costs is a section of its own below; it is a fact about the
`script` param, not about the script.

**Ring Array** takes a piece of geometry, a radius and a count, and copies the
geometry to a ring of points — rotating each copy to face outward. Wire a stock
**Polygon** component into `Geometry` and you get a rosette. Three inputs, two
outputs, one of each a geometry type: enough to make the setup cost real.

Measured on **Rhino 8.34.26223.11002 / Grasshopper 1.0.0008**, macOS, 2026-08-25,
against live `CSharpComponent` and `Python3Component` instances on an open canvas.

## Picking this up cold

**What this is for.** The kit's normal answer to "get source onto a component" is Script
Forge plus an MCP-driven `forge-push`. This folder deliberately uses none of that. The
question it answers is the one underneath: *with nothing but stock Rhino 8, how much can a
script component configure about itself, and what is genuinely out of reach?* Everything a
forge does is somewhere on that map — so knowing where the native ceiling sits is what
tells you which parts of the forge are convenience and which are capability. The answer, in
one line: params and identity are all reachable from a script; **documentation and identity
have no UI at all**, and outputs are undeclarable in Python.

**The rig.** `native-ceiling.gh`, in this folder — a Grasshopper canvas with the demo
components on it, plus sliders and a Polygon feeding them. It is tracked (one of two
`.gh` exceptions in `.gitignore`), but it is still not a build input, and a tracked
snapshot is not a guarantee: a live session drifts from it the moment you edit anything,
and the working copy may be an unsaved *unnamed* document. Never assume it matches; check.
Enumerate what is actually there with:

```csharp
foreach (var o in Grasshopper.Instances.ActiveCanvas.Document.Objects
                 .Where(o => o.GetType().Namespace == "RhinoCodePluginGH.Components"))
  Console.WriteLine(o.GetType().Name + " '" + o.Name + "' " + o.InstanceGuid);
```

Reaching the canvas at all needs `MCPStart` run in Rhino first; until then every
`mcp__rhino__*` call fails with *"Could not connect to Rhino"*. That is the normal first
failure of a session and the fix is one command from the user.

**The one constraint that governs how you can test anything here.** Building params *from*
a signature happens on an **editor save** — a human pasting and closing the ScriptEditor.
It is driven by language servers inside the editor, and nothing else triggers it:
`IScriptComponent.set_Text` moves the text and leaves the params where they were, in both
languages. So **an agent cannot test declaration behaviour on its own.** If a question
turns on what pasting does, write the file and ask the user to paste it; anything else
measures a different code path and will report that pasting configures nothing, which is
false. Everything in this note about *pasting* was checked by hand for that reason.

What an agent *can* do unaided is the whole imperative half — set hints, access, tooltips,
identity, persistent data by reflection, solve, and read the result back. That includes
running the solve in reverse as an oracle to learn Grasshopper's own spelling for a param
state; see [csharp-type-hints.md](../../docs/write-scripts/csharp-type-hints.md), *The
signature as an oracle*, including the one-solve readback lag that will otherwise waste
your time.

**Don't diagnose from `ScriptType.Text`.** On a linked component that is the copy archived
into the `.gh`, not what executes — it reads stale on a menu-set param that is running
current code. See *Checking what a component is actually running* below for the reliable
routes.

## Where param metadata comes from

Grasshopper keeps a script component's params as state on the component, separate from the
source.

**A type hint is not a fact about the code — it is a Converter selected on the param.** It
lives at `ScriptVariableParam.TypeHints` on the component, `TypeHints.GetSelected().TypeName`
names the current choice, and it is archived into the `.gh` and restored on open. So when a
C# signature says `double Radius`, that is a *declaration the editor reads once*, at paste
time, and converts into a selection on the `Radius` param. **From then on the param owns
the typing.** The code is regenerated from the param on every solve, never the other way
round, and a hint set by hand from the right-click menu is exactly as real as one that
arrived from a signature. Everything else in this note follows from that split: the source
is a way to *deliver* param state, and once delivered the source no longer holds it.

Three different things move that state, in different directions, and almost every
confusion about script components comes from conflating them:

| trigger | direction |
|---|---|
| **editor save** — a human pastes source and closes the editor | **params ← signature.** C# both sides; SDK-mode Python inputs only, and script-mode Python not at all |
| **solve** | **signature ← params**; every argument is retyped from its param's hint, and an unhinted param becomes a plain `object` |
| **programmatic `set_Text`** — Script Forge, or any reflection-driven push | neither. The text lands, the params do not move, and the next solve rewrites the signature from them |

### Both languages declare their params — Python only in SDK mode

A C# `RunScript` signature names every param and types all five. **The ScriptEditor
reads it.** Paste `ring-array-stock.cs` into a fresh C# Script component, close the
editor, and the component comes back with `Geometry` / `Radius` / `Count` → `out` /
`Points` / `Arrayed` already built, in order, with `GeometryBase` / `double` / `int`
selected on the inputs **and `Point3d` / `GeometryBase` at list access on the outputs** —
element type and access both read off `out List<Point3d>`. It solves correctly, and
nothing was configured by hand. The sync is
`RhinoCodeEditor.Editor.RCEState+StateCode.SetCodeParamsFromServers()`: it lives in the
**editor**, driven by language servers, not on the component.

**Python has a signature too, once the script is in SDK mode**, and the editor reads it
the same way. A *script-mode* Python file — bare statements at module level — genuinely
declares nothing: there is no signature, pasting it leaves the params at `x` / `y`, and
the first input it touches raises `NameError`. But right-click ▸ **Convert To
GH_ScriptInstance** (or writing the class by hand) wraps the body in

```python
class MyComponent(Grasshopper.Kernel.GH_ScriptInstance):
    def RunScript(self, Geometry: Rhino.Geometry.GeometryBase, Radius: float, Count: int):
```

and that argument list is read on paste exactly like C#'s. Measured on a fresh Python 3
component: the three inputs came back named `Geometry` / `Radius` / `Count`, in order,
carrying the GH type hints `GeometryBase` / `float` / `int`.

The annotations are ordinary Python type hints, and Rhino's own guide says they are
["only for static analysis … and does not have any effect of the script
execution"](https://developer.rhino3d.com/guides/scripting/scripting-gh-python/#python-3-type-hints).
That is true of *execution* and badly undersells the editor: the same page's *RunScript
Signature* section is the accurate one — the signature carries the inputs "by their name
and data type (based on their Type Hints)". Annotating a Python argument selects a
converter on the param, and it survives every later solve.

**Access is declarable too.** The container spelling around the annotation sets it:

| access | annotation |
|---|---|
| item | `Count: int` |
| list | `Count: list[int]` |
| tree | `Count: Grasshopper.DataTree[int]` |

An **unannotated** argument is not "no hint" — it becomes `object` (*ghdoc Object*), and
Grasshopper writes it back as a bare name with no annotation at all. Inside a container an
unhinted param spells as `object`: `list[object]`.

The annotation vocabulary is Grasshopper's own — harvested by setting each hint on a live
param and reading back the signature it regenerated:

| hint | annotation |
|---|---|
| `bool`, `int`, `float` | `bool`, `int`, `float` |
| `string` | `str` |
| `object`, or no hint | *(omit the annotation entirely)* |
| every geometry type | `Rhino.Geometry.<X>` — `Point3d`, `Curve`, `Brep`, `GeometryBase`, … |
| `Color` | `System.Drawing.Color` |
| `Guid` | `System.Guid` |
| `Complex` | `System.Numerics.Complex` |
| `DateTime` | `System.DateTime` |
| `Point3dList` | `Rhino.Collections.Point3dList` |
| `UVInterval` | `Grasshopper.Kernel.Types.UVInterval` |

**What Python cannot declare, and C# can:**

- **Outputs — at all.** No name, no hint, no access. `return Points, Arrayed` returns
  *values*; the identifiers are local names and nothing reads them. The link is one-way
  at best: adding an output param on the component does not rewrite the `return`
  statement. C# declares its outputs fully, from `out List<Point3d>`.
- **`optional` and defaults.** A Python default (`Radius: float = 5.0`) is not just
  ignored — it **costs you the type hint**. Do not write defaults into a `RunScript`
  signature.

  Re-measured by hand 2026-08-26 with a **control** in the same paste, which pins down
  what the default is and is not responsible for:

  ```python
  def RunScript(self, Control: float, Defaulted: float = 5.0, Curvy: Rhino.Geometry.Curve = None):
  ```

  | param | hint after the paste | annotation GH regenerated |
  |---|---|---|
  | `Control: float` | **`float`** — intact | `Control: float` |
  | `Defaulted: float = 5.0` | **`object`** — lost | `Defaulted` (bare) |
  | `Curvy: … = None` | **`object`** — lost | `Curvy` (bare) |

  Three things that refines:

  - It is **any** default, not just a numeric one — `= None` on a geometry annotation
    costs the hint identically.
  - **`Optional=True` is not part of the damage.** The control came back `Optional=True`
    too; that is simply what a pasted param is, not something the default caused.
  - You **gain nothing** for the loss. Persistent data was `0` on all three, so the value
    never becomes the param's default, and GH strips the `= 5.0` out of the regenerated
    signature — so it does not even survive as documentation. Use a header `"default"`
    instead, which does reach persistent data.

**What the editor sync does *not* do**, measured on the same paste:

| | after pasting the C# file |
|---|---|
| param names, order, type hints, access | **built from the signature** |
| per-param `ToolTip` | **empty** — `Description` reads the converter's generic text, *"Converts to collection of generic geometry"* |
| component `Name` / `Description` / `Tooltip` | **untouched** — still `C# Script` / *"C# scripting component"* |
| icon | untouched |

### The solve rewrites the other way

**Grasshopper edits your source text on its own, every time the component solves.** This
is a real write to the source the component stores, not a compile-time transform: paste a
file, let it solve once, reopen the editor, and the `RunScript` line is not the one you
pasted. It has been regenerated from the params — retyped, reflowed across lines, and
re-indented with tabs. Nothing prompts, and nothing reports it. A component's source and
the file it came from therefore **diverge the moment it first solves**, even when the
paste was exact, which is why comparing the two needs the signature blanked first (see
*Checking what a component is actually running*). The body is left alone; only
`RunScript`'s argument list is rewritten.

**This applies to SDK-mode Python too**, and that is easy to miss because script-mode
Python is never touched. Measured on the pasted stock Python file: a signature written
on one line on disk came back as

```python
    def RunScript(self,
            Geometry: Rhino.Geometry.GeometryBase,
            Radius: float,
            Count: int):
```

Annotations are regenerated from the params on the same terms as C#'s types — an
`object`-hinted or unhinted param comes back as a **bare** argument with no annotation,
list access as **`list[T]`** — measured 2026-08-26, e.g. `Geo: list[Rhino.Geometry.GeometryBase]`,
not `System.Collections.Generic.List[T]`. Tree access is
`Grasshopper.DataTree[T]`. The `return` statement is never rewritten, because no param
state corresponds to it.

**But not every param edit reaches the signature, and the exceptions are not obvious.**
Measured 2026-08-26 on a live SDK-mode Python component: changing a param's **type hint**
propagates on the next solve, while changing its **access** does not — `Geo` sat at
`access=list` through three consecutive solves and the signature still read
`Geo: Rhino.Geometry.GeometryBase`. Adding a param unfroze it, and the very next readback
gave `Geo: list[Rhino.Geometry.GeometryBase]`. So:

| in-place edit to a param | reaches the regenerated signature? |
|---|---|
| type hint | **yes**, on the next solve |
| access (item / list / tree) | **no** — frozen until a param is added or removed |
| `NickName` / `VariableName` | **no** — same freeze |

This is why a signature can disagree with the params behind it and stay that way
indefinitely. Never read access off the signature; read it off the param.

Whatever the editor built, every solve regenerates the argument list *from the params*. So
a param's hint is the only durable typing, and a param with no hint produces a plain `object`
argument no matter what the file says. With params correctly *named* but unhinted,
`RunScript(GeometryBase Geometry, double Radius, int Count, …)` came back rewritten to
`object` for every argument and the body failed on *Operator '<' cannot be applied to
operands of type 'int' and 'object'*.

**Rewritten text is not a cleared hint.** The two are separate storage, and this is the
easiest thing here to get backwards. An **output** is regenerated as `ref object` no
matter what hint its param holds, because outputs are write-only sinks — so `ref object`
in the body is not evidence of an unhinted param. For an output it is evidence of
nothing at all. Measured on the pasted stock version, the live source reads

```csharp
  private void RunScript(
		GeometryBase Geometry,
		double Radius,
		int Count,
		ref object Points,
		ref object Arrayed)
```

while the params behind it still hold `Point3d` / list and `GeometryBase` / list, and go
on holding them through every later solve.

What the output hint does *not* buy is any change downstream. Measured on the
auto-configured C# component before and after it gained output hints — same body, same
wiring, only the hint changed — the outputs emitted 9 unwrapped `GH_Point` and 9
`GH_Curve` on one path both times. The stock version, hinted at *list* access, unwraps
its `List<T>` the same way. On C# an output hint is real, durable, declarable state that
changes neither the body nor the data; the `List<T>` assigned to a `ref object` output is
unwrapped regardless.

Both routes to an output hint work, and the stock and auto-configured versions use one
each. The stock version declares it — `out List<Point3d>` in the signature, read by the
editor on paste. The auto-configured version sets it imperatively, `TypeHints.Select(…)`
against the output param, exactly as it does for the inputs; the same call is *pointless
but harmless* on a Python output, not forbidden — see *A hint on a Python output does not
break lists* below. The auto-configured version's outputs come back at `item` access
rather than `list`, because nothing sets output access
— which changes nothing, as above.

### `set_Text` is not an editor save

This is the trap for anyone testing by reflection, and it is why Script Forge stamps
every param itself rather than trusting the source it pushes. A programmatic push moves the
text and nothing else; a test built on it reports that pasting configures nothing, which
is true of that path and false of the one a person actually uses. Everything in this note
that concerns *pasting* was checked in the editor by hand.

## Stock → Auto-configured: what auto-configuring buys you

The stock version configures nothing — no param name, no type hint, no tooltip, no
identity. What happens when you paste it depends entirely on the language:

| pasted into the editor of a fresh component | C# | Python 3, SDK mode | Python 3, script mode |
|---|---|---|---|
| **input** params built | **yes** — names, order, hints, access, from the `RunScript` signature | **yes** — same, from the same signature | **no** — params stay `x` / `y` |
| **output** params built | **yes** — names, hints, access, from `out List<T>` | **no** — outputs are undeclarable | **no** |
| does it run | **yes**, correctly | inputs bind; **outputs must be built by hand** | **no** — `NameError: name 'Count' is not defined` |
| param tooltips | no — generic converter text | no | no |
| component name, description, icon | no | no | no |

So a C# component is usable straight from a paste, an SDK-mode Python one is half way
there — every input for free, every output by hand — and a script-mode Python one gets
nothing. What none of them gets is the documentation half — every tooltip, and the
component's own name, description and icon — and three of those have **no UI
whatsoever**:

| | stock | auto-configured |
|---|---|---|
| component `Name` | **impossible by hand** — stays `C# Script` / `Python 3 Script`, and it is the tooltip's title | `Ring Array` |
| component `Description` | **impossible by hand** — stays *"C# scripting component"* | set |
| icon | **impossible by hand** | a drawn 24×24 bitmap |
| five param tooltips | right-click menus, one at a time | free |
| input params, hints, access | free in C# and in SDK-mode Python | free in both |
| output params, hints, access | free in C#; right-click menus in Python | free in both |

The auto-configured version is the same logic plus about 120 lines that apply all of it at solve time,
every solve, self-healing after a reopen. Its C# param-building bootstrap is therefore
usually a no-op when the file is pasted through the editor; it earns its keep when the
params are wrong, when the source arrives some other way, and on the Python side for the
outputs always.

### Building the params by hand

A C# paste arrives with every param already built. An SDK-mode Python paste arrives with
its **inputs** built and its outputs still at the stock `a`, so only steps 1–2 below apply
to it, and only to the output side. A script-mode Python file needs all of it.

1. Zoom in and click `+` / `−` on the parameter gutters until there are three inputs
   and two outputs. The second output goes *after* the built-in `out` print stream.
2. Right-click each param. The top textbox is the name — and on a script component the
   name is also the identifier the code binds to, so it must match exactly:
   `Geometry`, `Radius`, `Count` → `Points`, `Arrayed`.
3. Right-click again → *Type Hints* → `GeometryBase`, `float`, `int` for the three
   inputs. Leave the outputs unhinted — not because a hint is destructive (it is not;
   see *A hint on a Python output does not break lists* below) but because it buys
   nothing.
4. Optionally *Name (for humans)* and *Tooltip (optional)* on each param — five more
   dialogs. That is what the auto-configured version does for free.

The component's own `Name`, `Description` and icon have no dialog at any point.

## Component level

| what | from the script | right-click UI | survives save + reopen |
|---|---|---|---|
| `NickName` | `Component.NickName = …` | textbox at the top of the menu | yes |
| `Name` | `Component.Name = …` | **no UI at all** | yes |
| `Description` | `Component.Description = …` | — | **no** — regenerated from `Tooltip` on load |
| `Tooltip` *(lowercase t)* | reflection in C#, plain attribute in Python | *Tooltip (optional):…* | **yes** — and it overwrites `Description` on open |
| icon | `((GH_DocumentObject)Component).SetIconOverride(bmp)` | — | **yes**, the bitmap is archived |
| `IconDisplayMode` | `= GH_IconDisplayMode.icon` | — | yes |
| `Message` (grey caption) | `Component.Message = …` | — | **no** — comes back empty; re-set every solve |
| hide the `out` print param | — | *Standard Output/Error Parameter ("out")* | yes |
| `Category` / `SubCategory` | settable — and **inert** | — | ribbon placement comes from the registered proxy, not the instance |
| `Exposure`, `Keywords` | get-only | — | — |

Write **both** description slots. `Description` is what the tooltip shows right now;
`Tooltip` is what is archived and restored over it on the next open.

## Parameter level (`ScriptVariableParam`)

| what | from the script | right-click UI | survives save + reopen |
|---|---|---|---|
| identifier **and** what GH draws | `p.VariableName` (== `NickName`) | textbox at the top | yes |
| human name (tooltip title) | `p.PrettyName` (== `Name`) | *Name (for humans, optional):…* | yes |
| tooltip body | `p.ToolTip` *(capital T)* | *Tooltip (optional):…* | **yes** |
| tooltip body, via `Description` | `p.Description = …` | — | **no** — replaced with the converter's generic text |
| type hint | `p.TypeHints.Select("double")` | *Type Hints ▸* | yes |
| access | `p.Access = GH_ParamAccess.list` | *Item / List / Tree Access* | yes |
| required vs optional | `p.Optional = false` | *Required* | yes |
| default value | `p.SetPersistentData(…)` | *Set Data Item…* / *Internalise data* | yes |
| wire display, Reverse / Flatten / Graft / Simplify, Principal | plain properties | menu | yes |
| add / remove params | `CreateParameter` + `Params.RegisterInputParam` | zoom-in `+` / `−` | yes |
| `Keywords` | get-only | — | — |

**A new param's starting hint depends on where it came from**, which is the whole reason
the demo components disagreed with each other before this was pinned down:

| the param arrived… | starting hint |
|---|---|
| on a stock component dropped from the ribbon (`x`, `y`, `a`) | `object` |
| from `CreateParameter` — the zoom-in `+`, or a script building its own | **`No Type Hint`** |
| from an SDK-mode Python annotation | whatever the annotation names |
| from an *unannotated* SDK-mode Python argument | `object` |

None of the four affects emitted data on an output, so a mixture is harmless — but it is
why one component can read `object` and its neighbour `No Type Hint` with identical
sources. Neither is a leftover to clean up; they are just different provenance.

**What Grasshopper draws *is* the code identifier.** `NickName` and `VariableName` are the same
storage, and Grasshopper's `RenderComponentParameters` draws `NickName` and nothing
else. A param cannot draw *Point count* while the code reads `Count`. `Name` /
`PrettyName` only changes the tooltip's **title**, which Grasshopper renders as
`Name (NickName)` whenever the two disagree.

**Rename with `SetVariableName(…)`, not the `VariableName` property.** The method is
what the right-click rename box calls and it re-syncs the component's binding at once;
writing the property leaves a renamed **output** unbound until the source is pushed
again — silently, with no error. Python cannot reach the method (it is non-public), so
the Python versions write the property and let the rebuild's own re-solve settle it.

## C# against Python 3

| | C# Script | Python 3 Script |
|---|---|---|
| does the source declare its params? | **yes** — inputs *and* outputs: the `RunScript` signature carries names, order, types and access, and the editor builds the params from it | **inputs only, and only in SDK mode.** Names, order, hints and access come off the `RunScript` signature; outputs are undeclarable and script mode declares nothing |
| GH rewrites the source | **yes** — `RunScript`'s argument list, every solve | **yes, in SDK mode** — same argument list, same every solve. Script mode is never touched |
| bootstrap needs fallback declarations | yes — see below | no |
| reaching `ScriptVariableParam` members | **reflection only.** The sandbox does not reference `RhinoCodePluginGH`, so `using RhinoCodePluginGH.Parameters;` is a `CS0246` | plain attributes — Python.NET binds by the runtime type |
| `SetIconOverride` | needs a `(GH_DocumentObject)` cast — not on `IGH_Component` | direct |
| an unwired input | a default-valued argument: `0.0`, `false`, `null` | **`None`, in both modes** — provided a param of that name exists. Script mode injects the name into the namespace bound to `None`; SDK mode binds the argument to `None`. Either way `range(Count)` raises *'NoneType' object cannot be interpreted as an integer*. What raises `NameError` is a name **no param carries** — see below |
| type-hint vocabulary | 35 converters | 36 — adds `object`, in the menu as *ghdoc Object* |
| hint spelling | `double`, `string` | `float`, `str` |
| output hints | **declarable and durable** — set from `out List<T>`, kept through every solve; but they change neither the body (`ref object`) nor the emitted data | **not declarable**, and settable only imperatively. Harmless when `MarshOutputs` is on — see below |
| the script input parameter | **does nothing** — see below | works |

Both menus label two converters differently from their API `TypeName`: *File Path* is a
second converter whose `TypeName` is also `string`, and *System.Drawing.Color* is
`Color`. **An unrecognised name handed to `TypeHints.Select` throws**
`InvalidOperationException` out of `ScriptVariableTypeHintSet.Find` — it does not fail
silently. Measured 2026-08-26 on a live
Python param: `Select("float")` and `Select("string")` are accepted, while
`Select("double")`, `Select("str")` and `Select("NotAHint")` all throw, leaving the
previous hint in place.

That result exposes **three different spellings** for the same converter, and they do not
line up — this is the easiest thing here to get wrong:

| vocabulary | `double`-ish | `string`-ish |
|---|---|---|
| this kit's header `type` (the forge resolves either) | **`double`** canonical, `float` accepted | **`string`** canonical |
| `TypeHints.Select(…)` on a live **Python** param | **`float`** — `double` throws | **`string`** — `str` throws |
| the annotation GH regenerates into a Python signature | `float` | `str` |

So `string` is the API name but `str` is the annotation, and `float` is the API name but
`double` is the header's canonical spelling. Only the middle row throws when you get it
wrong; the other two fall back or resolve.

### `NameError` is about the param, not about the wire

Script mode binds an unwired input that *exists* — it is a missing **param**, not a
missing wire, that raises `NameError`. (On a fresh paste the params are still `x` / `y`,
so the name genuinely does not exist; that case is the fresh-paste row above, not a rule
about wires.) Measured 2026-08-26 on a live `Python3Component` whose params were built by
reflection and left unwired:

| the name refers to | script mode | reading it bare |
|---|---|---|
| a param that **exists**, unwired, `Optional`, no persistent data | bound to **`None`** | **safe** — `Count if Count else 3` yields `3` |
| a param that **does not exist** | never bound | `NameError: name 'Missing' is not defined` |

Measured with a hinted `int` param and a `GeometryBase` param side by side; both came back
`in_globals=True`, `value=None`. So `globals().get(…)` earns its keep in exactly one place:
a script that **builds its own params**, whose first solve runs before the param it is
about to create exists. That is why `ring-array-auto-configured.py` uses it and why the
ordinary headered examples do not need to.

Note that `None` is not the same as the fallback a tooltip promises. An input documented as
"defaults to 3 when unwired" is only telling the truth because the body writes `else 3`; a
header `"default"` is what makes the promise real, and it is the only route by which an
unwired `bool` can read `true` — `None` and a wired `false` are both falsy.

### A hint on a Python output does not break lists — `MarshOutputs` does

This one is worth stating carefully, because the obvious diagnosis is wrong and it is
easy to arrive at honestly. The rule usually written down is *"never hint a Python output
— an active converter collapses a list result."* Measured against all 35 converters on an
output emitting a nine-point list, **no hint collapses anything.** The converter is
applied **element-wise**: nine `GH_Point` became nine `GH_Vector`, nine `GH_Number`, nine
`GH_String`, nine `GH_Box`. Only conversions that are genuinely impossible — `Curve`,
`Guid`, `Point3dList` from a `Point3d` — fail, and they fail *loudly*, with
*Parameter "a" type conversion failed from Point3d to Curve* and an empty output.

The thing that actually collapses a list is the component's **`MarshOutputs`** flag:

| `MarshOutputs` | hint | result |
|---|---|---|
| `True` | any convertible hint, or none | **9 items**, unwrapped |
| `False` | `object` / no hint | **1** `GH_ObjectWrapper` holding the whole list |
| `False` | `Point3d` | **empty** |

Which explains where the rule came from. `set_Text` clears all three `Marsh*` flags on a
component that is **not yet in the document** (measured 2026-08-26; an in-document push
leaves them alone), so a create-then-push onto a component whose output happened to carry
a hint produces exactly the bottom row — an empty output — and the hint is the visible
difference. It is the flag. Add the component to the document before pushing, or restore
`MarshInputs` / `MarshOutputs` / `MarshGuids` afterwards, and hints on Python outputs are
as inert as they are on C# ones. See
[python3-marshalling.md](../../docs/write-scripts/python3-marshalling.md) for the split.

Leaving Python outputs unhinted is still the right default — it matches stock components,
and an output hint buys nothing in either language — but the reason is "it is pointless",
not "it is destructive".

### The two-pass bootstrap, and why only C# needs a trick

The params must exist before the code can bind to them, so a self-configuring script needs
two solves: pass 1 builds the param list and schedules another solution, pass 2 runs for
real. Do the surgery **outside** the solution (`ScheduleSolution`) or GH 8's *object
expired during a solution* guard locks the canvas.

On pass 1 the C# params are still `x` / `y` / `a`, so Grasshopper has already rewritten
`RunScript` to take those — and a body referencing `Radius` would not compile, which
means the bootstrap could never run. Same-named **fallback fields** on the class break
the deadlock: they satisfy the compiler on pass 1 and are shadowed by the real arguments
from pass 2 onward.

```csharp
GeometryBase Geometry;
double Radius;
int Count;
object Points, Arrayed;

private void RunScript(GeometryBase Geometry, double Radius, int Count,
                       out object Points, out object Arrayed)
```

One ordering rule follows, and it is easy to get wrong: **select the type hints inside
the rebuild, not merely on the next solve.** Grasshopper rewrites the argument list the
moment the params change, and an unhinted param becomes a plain `object` argument — the body
then fails to compile on `i < Count` and never gets the chance to set them.

Python needs none of this. A name that is not bound yet is only a problem on the line
that touches it — which is also why the auto-configured version can stay in script mode and build the whole
param list in one imperative pass. An SDK-mode signature would hand it the three inputs
for free, but not the two outputs, so the bootstrap would survive either way.

## Reading the source off disk

Right-click ▸ **Script Parameter** gives the component a `script` param you point at a
file.

### A file that is *set* is a snapshot; only a file that is *wired* follows

**Set the file from the param's right-click menu and the component never re-reads it.**
It executes whatever it resolved at the moment the file was set, indefinitely — through
recomputes, through `ExpireSolution`, through anything short of setting the file again.
Measured by editing a param tooltip on disk and reading it back off the live component:

| | tooltip on the component |
|---|---|
| file **set** from the menu, edited on disk, no recompute | **stale** |
| file **set** from the menu, edited on disk, `ExpireSolution(true)` + a full solve | **still stale** |
| file **wired** in, `Input Is Path` + `Synchronise` on, edited on disk | **current** — no recompute needed |

A recompute is not a re-read. The staleness is invisible while it happens: the param
still points at the right URI, the component still solves, and its params still match the
source it is running — just not the source on disk. Two components pointing at the same
file can run different code indefinitely, and nothing on the canvas says so.

#### The two toggles are greyed out until something is wired in

The `script` param's menu carries **`Synchronise`** — *"When checked, this parameter
triggers an update when the file(s) on the disk change"* — and **`Input Is Path`** —
*"When checked, input string is treated as script path"*. On a param whose file was
**set** from the menu, both are **disabled**, and that is the trap: the toggle that would
fix the staleness is visible, describes exactly the behaviour you want, and cannot be
clicked.

They are gated on the param having a **wired source**. Measured on one param, before and
after connecting a panel to it:

| `SourceCount` | `Synchronise` | `Input Is Path` |
|---|---|---|
| 0 — file set from the menu | disabled | disabled |
| 1 — path wired in | **enabled** | **enabled** |

So the live-reloading workflow is not the menu one. Wire a path in — a panel or a
**File Path** param, *not* a Read File component, which yields a file's contents rather
than its path — then switch on `Input Is Path` and `Synchronise`. The param resolves the
path to a script, attaches a file watch, and follows every edit on disk with no recompute
needed. Verified end to end: editing the file moved the resolved length 11175 → 11187 on
its own, and reverting it moved it back.

Under the hood these are `TreatInputAsPath` and `ExpireOnDiskEvent` on
`RhinoCodePluginGH.Parameters.BaseParam<,>`, with the watch itself in
`BaseScriptParam._storages`. Both properties are writable by reflection, but **doing so
on a menu-set param does not stick** — the next solve recomputes them back to `False` and
tears the watch down, so it fires at most once. A wired source is what makes the state
durable; the watch then survives a full `NewSolution(true)`.

> **Don't diagnose any of this from `ScriptType.Text`.** That property is the copy
> archived into the `.gh`, not what executes — it reads stale on a menu-set param that is
> running current code. Check a param tooltip, or any other observable the source
> controls.

### What a linked script does not carry

**A file on disk carries source text and nothing else.** The script object the param
resolves reports `Inputs` = 0, `Outputs` = 0, an empty `NickName` and an empty
`Description`; its `Name` is just the filename. So the auto-configured version's self-configuring work is
exactly as necessary here — it just has to survive two things.

**And an SDK-mode signature buys nothing on this path.** `Inputs` = 0 / `Outputs` = 0 is
reported for an SDK-mode file too — the param-building sync lives in the editor and fires
on an *editor save*, which reading a file off disk never performs. Confirmed twice over:
`RhinoCode.CreateCode(uri)` returns a plain `McNeelPythonCode` with zero params for both
modes, and a programmatic `IScriptComponent.set_Text` of an SDK-mode source leaves the
params at `x` / `y`. **Declaration is an editor feature, not a language feature** — which
is why the auto-configured version stays in script mode and builds its params imperatively.

- **The param is inserted at input index 0** and shifts the real inputs down one. It is a
  `RhinoCodePluginGH.Parameters.ScriptParam`, not a `ScriptVariableParam` — no
  `VariableName`, no `PrettyName`, no `ToolTip`. Any script that walks `Params.Input` by
  position writes its first param's name onto the script param, drops the last input, and
  shifts every remaining name up by one. **This is not theoretical.** Measured on a live
  component running from
  a `script` param: the inputs came back `script, Radius, Count` with `Geometry` silently
  gone. Worse, the shape check that guards the rebuild compares against
  `Geometry, Radius, Count` and can therefore *never* pass — the `script` param has no
  writable name — so every solve rebuilt the params and scheduled another solve, forever.
  A component that has quietly eaten an input and is spinning at 100% looks, on the canvas,
  like a component that is merely slow.
- **The param arrives holding the component's own script as item 0.** It is *item* access,
  so two items means Grasshopper iterates the component twice and runs both scripts.
  Wiring the path in fixes this one for free: a wired source replaces the param's
  persistent data outright, so the param resolves to exactly one script and the stock
  entry is gone. Measured — the same param went from two items to one on being wired.

The auto-configured version handles the first by counting the leading params Grasshopper owns instead of
assuming index 0, and reports the second as a warning. Measured across all four
configurations, the count is right every time — `0` for pasted inputs, `1` for linked
inputs, `1` for outputs with `out` shown, `0` with `out` hidden — because it reads param
*types* rather than positions:

```python
def gh_owned(params):
    n = 0
    for p in params:
        if p.GetType().Name == "ScriptVariableParam":
            break
        n += 1
    return n
```

That single function is the whole of what "hardening for the script param" amounts to.
It is not script-param handling at all — it is the `NickName`-over-index rule
applied to the one path a name lookup cannot reach, the one that *creates* and *destroys*
params rather than reading them. Every other place these files touch a param already looks
it up by name:

```python
p = next((q for q in comp.Params.Input if q.NickName == name), None)
```

which is immune to `script` and `out` alike, because it never asks where anything sits.

### ⚠ On a C# component the script param does nothing

Verified on an otherwise identical rig: a C# Script component with the script param
enabled executes **nothing** from the file. Every output null, no `print`, no error, no
diagnostic — while the file resolves correctly (`HasCode` true, `HasBuildError` false,
right language spec, right length). Confirmed with a byte-identical copy of the
component's own stock template, so it is not the shape of the file, and the identical
rig on a Python 3 component runs correctly.

Re-confirmed against a *correctly wired* rig — path wired in, `Input Is Path` and
`Synchronise` both on, the param resolving the current file — and the component is still
inert: params stay at the stock `x` / `y`, output `a` empty. So this is not a symptom of
a stale or mis-set script param; it survives the setup that fixes staleness.

So the linked half of `ring-array-auto-configured.cs` is theory: the `GhOwned` /
`ReportSource` handling is there so the two languages read the same, and because it costs
a pasted component nothing. Paste the C# file; link the Python one.

## Checking what a component is actually running

Nothing on the canvas tells you which revision of a file a component holds, and the two
languages fail differently when it is wrong — Python rebuilds its params from whatever
source it has, so a stale component looks *correct*, just with the wrong param list. Work
from the source text, and get it from the right place:

| component | where the live source is |
|---|---|
| pasted | `IScriptComponent.Text` on the component |
| linked, path **wired** in | the resolved script on the `script` param — current, and re-read on every disk change |
| linked, file **set** from the menu | the resolved script is a snapshot, and `ScriptType.Text` is the copy archived into the `.gh` — trust neither |

Compare by hash rather than by eye, with two normalisations, or every comparison fails
for the wrong reason:

1. **Blank the `RunScript` argument list.** Grasshopper rewrites it on every solve, so it
   never matches the file. Scan from `private void RunScript` to the matching `)` and
   replace the span. Script-mode Python needs nothing here; **SDK-mode Python needs the
   same treatment** — its `def RunScript(...)` argument list is rewritten on every solve
   too, and a one-line signature on disk comes back reflowed across several lines.
2. **Strip all whitespace.** The rewrite reflows the signature across lines and re-indents
   with tabs.

Then SHA-256 what is left, on both the component and the file, and compare. Verified this
way on 2026-08-25: all six components matched their files exactly. The digests are not
reproduced here on purpose — they are a point-in-time check, and any edit to a source
file (the SDK-mode rewrite of the stock version, for one) invalidates every one of them. Recompute
both sides when you want the check; a pinned digest in a document is only ever a
liability.

The cheap partial check, when a hash is more machinery than you want: read a param tooltip
off the live component. It is set by the source, has no other origin, and a stale
component shows the old text.

## Where the ceiling is

Everything a forge does to a script component, a pasted script can do to itself — params,
hints, access, defaults, names, tooltips, icon — because it is the same API surface, and
all of it survives a save and reopen.

**Both languages get part of the way there declaratively**, and that is worth saying
plainly: the `RunScript` signature *is* a header Grasshopper reads. Pasting it builds the
params, their order, their type hints and their access.

They stop in different places:

| declarable from the signature | C# | Python 3 (SDK mode) |
|---|---|---|
| input name, order, hint, access | yes | yes |
| output name, hint, access | yes — from `out List<T>` | **no** |
| `optional`, default value | no | no — and a default *removes* the hint |
| param tooltip | no | no |
| component name, description, icon | no | no |

So the ceiling is the same shape in both: no documentation, no identity. Python's is
lower by exactly one row — the outputs — and that row is the one that still forces a
two-pass bootstrap on the Python side, since the params must exist before the code can
bind to them.

Everything below the ceiling a pasted script can still do to itself — tooltips, names,
icon, defaults, and the outputs Python cannot declare — because it is the same API
surface, and all of it survives a save and reopen. Closing the gap in the script costs
two solves, a fallback-field trick in C#, a hint-ordering rule, and a reflection helper
per C# file. That, rather than any missing capability, is what a metadata header and a
forge pass buy.
