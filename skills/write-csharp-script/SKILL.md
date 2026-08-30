---
name: write-csharp-script
description: TRIGGER — load before creating or editing the body of a Rhino Grasshopper C# Script component (`.cs` authored for the "C# Script" component, or a root-level `.cs` in a rhino-gh-kit project), whenever: writing a fresh component from scratch; adding, renaming, or retyping a `RunScript` input or `out` param; touching `@component` header `"type"` hints, `"default"`, or `"optional"` for a C# param; or diagnosing a build/push error that is a symptom of these rules — `'object' does not contain a definition for 'Add'`, `Operator cannot be applied to operand of type 'object'`, `Index was out of range`, `Arithmetic operation resulted in an overflow`, or a `CS1003`/`CS0103`/`CS0246` traced to a quoted Description. SKIP for Python 3 Script components (no C# signature-rewrite or `out`-param quirk applies) and for pushing a finished file to the canvas (that's `forge-push`, not authoring).
allowed-tools: Read, Write, Edit, Grep, Glob, Bash
---

# Write a Grasshopper C# Script component

Rules for the *body* of a `.cs` file authored for Rhino Grasshopper's C# Script
component. This is authoring guidance, not the push mechanism — once the file
is ready, hand it to `forge-push`; never hand-write source directly onto a
live param to work around it.

Header grammar and the full `type` hint vocabulary live in
`${CLAUDE_PLUGIN_ROOT}/docs/write-scripts/header-reference.md`,
`${CLAUDE_PLUGIN_ROOT}/docs/write-scripts/csharp-type-hints.md`,
`${CLAUDE_PLUGIN_ROOT}/docs/write-scripts/naming-guide.md`,
`${CLAUDE_PLUGIN_ROOT}/docs/write-scripts/identity-properties.md` — this file
only covers what changes about the *body* as a result.

## The rules

- **PascalCase** all `RunScript` parameter names (inputs and `out` outputs) — e.g. `bool Refresh`, `out List<Guid> Guids`.
- **Every param gets a one-character `nickname`; every component gets a short one.** An
  omitted `nickname` falls back to the full `name`, so a compiled build draws
  `BasePlane` on the canvas where it should draw `P`. Take the **first letter of the head
  noun, capitalised** — the head noun, not the first word (`BasePlane` → `P`,
  `IgnoreZeros` → `Z`). Where the name is a role word (`Path`, `Location`, `Motion`) take
  the *type's* letter instead: `Path` (a `Curve`) → `C`. Uniqueness is **per side and
  case-sensitive**, so on a collision drop to the lowercase form for a curve parameter /
  index / interpolation weight (`t`, `i`), then to domain notation (`p`/`q`, `m`, `k`,
  `dt`), then to letter + ordinal (`Nx`/`Ny`, `C1`/`C2`). Sharing a nickname *across*
  sides is fine and often right — a pass-through `Geometry` draws `G` on both. **Never
  rename the param to dodge a collision**: `name` is the human label and outranks the
  nickname. Component `nickname`: an abbreviation or
  trim, roughly eight characters or fewer (`Curve Frames` → `Frames`, `List Stats` →
  `Stats`). Lexicon, component-nickname patterns and a worked example:
  `${CLAUDE_PLUGIN_ROOT}/docs/write-scripts/naming-guide.md`. Note which surface this reaches: a
  forged script component draws `variableName`, never `nickname`, so the nickname is the
  **compiled** build's label — set it when you write the header, not when you get around
  to compiling.
- **Always set descriptions for every input and output — in the header AND on the live params.** Write them once as the `"description"` of each object in the `@component` header's `"inputs"` / `"outputs"` arrays (plain language, for someone who doesn't know the component), then make sure they reach the canvas: `forge-push` stamps them from the header as part of every forge pass. A param whose tooltip still reads the generic "Converts to a collection of…" converter text is a defect.
- **Stamp the *durable* description slot — there are two at each level, and only one is archived into the `.gh`.**

  | level | volatile | durable |
  | ----- | -------- | ------- |
  | parameter | `IGH_Param.Description` | `ScriptVariableParam.ToolTip` — **capital T** |
  | component | `IScriptComponent.set_Description` | `BaseScriptComponent<,>.Tooltip` — **lowercase t** |

  For a **param**, write `ToolTip` and nothing else — it sets `Description` for the session too, and it is what survives a reopen and the next `set_Text` push. For the **component**, write `set_Description` **and** `Tooltip`: the first makes the tooltip right now, the second makes it right next open. There is **no "restamp after every push" rule** once the durable slots are written. `forge-push` writes both durable slots on every pass, so this is background knowledge for reading a component, not a procedure you carry out; mechanism and measurements in `${CLAUDE_PLUGIN_ROOT}/docs/write-scripts/identity-properties.md`.
- **Look params up by `Name`, not by index.** The built-in `Out` print stream lives at `Output[0]` when shown, but the user can right-click → "Standard Output/Error Parameter ('out')" to hide it, which removes it from the list and shifts every user-defined output index down by one. Index-based code (`Component.Params.Output[1].Description = ...`) then crashes with `Index was out of range` on every fresh open. Name-based lookup is immune.
- **An unwired `bool` item input collects `false`, not "nothing" — so any param documented as defaulting to *true* silently inverts.** GH hands `RunScript` a plain `false` whether the user wired `false` or wired nothing at all, so a `bool Overwrite` whose tooltip promises "true by default" refuses to overwrite on a fresh drop from the palette, and a `bool Run` gate can never mean "live until told otherwise". The argument alone cannot tell the two apart — and no amount of `SourceCount` inspection inside `RunScript` makes the *declared* default visible to the person reading the source. **Declare it in the header instead**, on the param object:

  ```json
  { "name": "Overwrite", "type": "bool", "access": "item", "default": true,
    "description": "Replace an existing entry. True unless you wire false." }
  ```

  A `default` becomes the param's **PersistentData** on a forged component and the value argument of the `Add*Parameter` overload in a compiled build, so the unwired param really does hand `RunScript` a `true`. The body then just reads `Overwrite` — no helper, no reflection, and the tooltip's promise is enforced by the same line that makes it. Available on `bool` / `int` / `double` / `string` only; see the header spec for why, and for the sibling key `"optional": false`, which instead makes an unwired input a hard stop (orange *failed to collect data*, `SolveInstance` never runs).

  **But first check the param is not presence-sensed, because a `default` is indistinguishable from a value the user internalised.** It lands in the same `PersistentData` slot that right-click → *Internalise data* writes to, so it answers *"what value?"* and can never answer *"did the user supply anything here at all?"*. Any param read through `SourceCount` / `PersistentDataCount` — directly or via a `Wired()`-style helper — is asking the second question, and a `default` makes its answer permanently *yes*, silently disabling whatever the probe was gating. That is not a fringe pattern: it is how you build a socket that **overrides** something else only while wired, and how you build an **interlock** that demands deliberate wiring before a destructive action. The kit's own Script Forge does it — `Target.SourceCount > 0` is what picks create-vs-update (`script-forge.md`). `Save and Restore State` hit both cases at once and correctly declares **no** defaults anywhere: a `default` on its `Mode` input would permanently override the right-click mode menu, and one on `Run` would stop its Replace interlock (`if (!Wired("Run"))`) from ever refusing to empty the ValueTable. So: presence-sensed param ⇒ no `default`, keep the helper. Value-with-fallback param ⇒ add the `default` and **delete** the helper — leaving both means the default quietly wins, which reads as the helper being broken.

  Two things follow. A `default` is close to **one-way**: Script Forge applies one only to a param that has no persistent data (so a user's override survives a re-forge), which also means the header cannot reset or remove a default from any canvas that already carries the component. And nothing checks this for you — `gh_meta.py --check` validates the header's shape, not what the body does with the param.

  This applies to **each** such input, not just the trigger: a project that fixes only its `Run` gate still ships every other "defaults to true" param inverted. Verified 2026-07-26 on Rhino 8: with `Overwrite` unwired a writer took its "already existed, left unchanged" branch and emitted null keys, contradicting its own header; wiring an explicit `false` toggle produced the same branch, confirming the two are indistinguishable from inside the body.
- **Keep the `Script_Instance : GH_ScriptInstance` class wrapper — a valid source file is not a bare `RunScript`.** The minimal envelope a pushed `.cs` must have is: the `@component` header comment, the `using`s (**including `using Grasshopper.Kernel;`** for `GH_ScriptInstance`), then `public class Script_Instance : GH_ScriptInstance { private void RunScript(...) { … } /* helpers, fields */ }`. Drop the class and `RunScript` becomes an invalid local function — the push compiles with `the modifier 'private' is not valid for this item` and `local function 'RunScript' is declared but never used`. `${CLAUDE_PLUGIN_ROOT}/examples/list-stats.cs` is the canonical skeleton.
- **Skip default template scaffolding — but not the class.** Drop the XML-doc summaries on the class and `RunScript`, the `Print`/`Reflect` stubs, the `private readonly` shadow fields for `RhinoDocument`/`GrasshopperDocument`/`Component`/`Iteration` (the base class provides them), the `// <Custom additional code>` markers, and any unused `using`s. Keep the `Script_Instance : GH_ScriptInstance` wrapper (above). Use a typed `RunScript` signature, not the weak-typed `(object x, object y, ref object a)` stub.
- **`out` params are write-only `object` sinks — compute in locals, assign once at the end.** On solve GH rewrites the signature so every `out` param is effectively `ref object`, regardless of its type hint (input hints *do* type the incoming data; output hints are decorative). So any operation on an `out` param other than a plain assignment fails to compile: `Guids.Add(...)` → `'object' does not contain a definition for 'Add'`; `Count++` / `Sum += n` → `Operator cannot be applied to operand of type 'object'`. Build the result in a local of the real type (`int count`, `double sum`, `var list = new List<T>()`) and assign to the `out` param once at the end. See `${CLAUDE_PLUGIN_ROOT}/examples/list-stats.cs` for the pattern.
- **GH rewrites the `RunScript` signature on solve.** Every time a C# Script component computes, GH replaces typed declarations in the body's `RunScript(...)` line with generic `ref object` forms (e.g. `out List<string> Keys` → `ref object Keys`). The actual *durable* typing lives in each param's **Converter** (right-click → Type Hint), not in the body declaration. But the sync runs both ways on different triggers, and a typed signature is **not** decorative: when a person pastes source into the **ScriptEditor**, the editor creates, names, orders and hints the params *from* the signature (`RhinoCodeEditor.Editor.RCEState+StateCode.SetCodeParamsFromServers()`), so a typed `RunScript` line is the one declarative route a script component has. A programmatic `set_Text` push takes neither direction — the text lands, the params do not move — which is why `forge-push` stamps every param itself. See `${CLAUDE_PLUGIN_ROOT}/docs/write-scripts/csharp-type-hints.md`, "Which way the sync runs". Disabled components skip the rewrite, so their body keeps whatever was last written. This is the root cause of the `out List<T>` quirk above: the body always sees `out object`, so `.Add(...)` won't compile and you must build a local list and assign at the end.
- **Implicit iteration appends the iteration index to output paths — an item/list-access component never path-mirrors its input tree.** Feed a list-access input a 30-branch tree `{0}..{29}` (one iteration per branch) and the outputs land at `{0;0}..{29;0}`, not `{0}..{29}`. Any rig that pairs trees **by branch path** downstream then silently matches nothing, because `{i;0}` never equals `{i}`. When a component's outputs must mirror its input tree, take the input as `DataTree<T>` (tree access), loop `Paths`/`Branches` manually, and emit on the **input** path — `EnsurePath(path)` for empty branches so the structure stays aligned. Applies equally to Python 3 Script components.
- **State that must survive a recompile goes in `GH_Document.ValueTable`, not statics.** Every source push rebuilds the assembly, so static fields reset — a stateful component (one tracking what it already did) redoes its work after each push or document reopen. Use `doc.ValueTable.GetValue(name, "")` / `SetValue(name, value)` keyed by `Component.InstanceGuid`; it persists in the `.gh` across saves and reopens.
- **The script context compiles with overflow checks ON.** `h *= largePrime` in a hash loop throws `Arithmetic operation resulted in an overflow` at runtime rather than wrapping. Wrap intentional wraparound math in `unchecked { … }`.

**Prefer the dedicated "C# Script" entry or dedicated "Python 3 Script" component over the generic "Script" entry** for new script components.

- **No double-quote `"` characters in component or param Descriptions.** Rhino's ScriptEditor plugin builder embeds each Description into generated C# as an **unescaped** string literal, so a `"` terminates the literal early and breaks the build (`CS1003`/`CS0103`/`CS0246`). Use single quotes/apostrophes instead. `gh_meta.py --check` refuses a quoted description on every route (Script Forge warns about the same thing), so it is a hard gate on `publish.sh`'s validate stage whether or not your build would have escaped it.

**Creating or updating a script component: `forge-push`, always.** One pass takes the `@component` header and the body to the canvas together — params, type hints, access, `default`s, identity, per-param tooltips and icon. It reuses the existing `IGH_Param` object for every name that did not change, so those wires never break at all; a rename is recycled positionally and keeps its wire; anything genuinely dropped is reported as `lost wire:` in the forge's Log. A hidden `Out` is handled correctly. Never hand-write source, or poke persistent data onto a script param, to work around it.

- **A param's type hint is a Converter selected on the param — `ScriptVariableParam.TypeHints` — and the signature is rewritten from it on every solve, so the hint is the only durable typing.** (The reverse also happens, but only on an editor save, and never on the `set_Text` push `forge-push` uses — see above.) `forge-push` selects it from the header's `type`; to *verify* one, read `ScriptVariableParam.TypeHints.GetSelected().TypeName` — the param's own `TypeName` reads `"Generic Data"` for a script param whatever hint is selected, so it is not the thing to check. The accepted `type` vocabulary (case-insensitive; `int`, `double`, `bool`, `string`, `Point3d`, `Vector3d`, `Plane`, `Interval`, `Curve`, `Brep`, `Mesh`, `GeometryBase`, … ~35 converters) lives in `${CLAUDE_PLUGIN_ROOT}/docs/write-scripts/csharp-type-hints.md`. **Python outputs are never hinted** — an output hint there is an active converter that breaks list outputs.
