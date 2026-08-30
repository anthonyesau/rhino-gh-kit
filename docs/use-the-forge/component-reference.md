# Script Forge — Component Reference

A reference for the **Script Forge** component itself: its three inputs, two
outputs, and the tree/branch data model that ties them together. It pairs with
the one-line tooltips stamped on each param — read those first for the gist,
this doc for the detail. The `@component` header format (how a *source* script
declares its own name, params, icon, etc.) lives in its own doc,
[`header-reference.md`](../write-scripts/header-reference.md); here the header
only matters where it changes what a param *does*.

> Script Forge takes C# or Python 3 **source** (as text or a file path) and
> **creates or updates** stock Grasshopper script components from it. Everything
> below is about driving that from the params — not about writing the source.
> Stock Rhino 8 Grasshopper, no plugins.
>
> Two rules govern everything the component does: **identity is declared, never
> remembered**, and **`Run` pushes on the rising edge and watches while it is
> held**. The forge stores nothing in the `.gh` and compares nothing — it is a
> command, not a function.

---

## The data model in one picture

Everything is organized around one idea: **one Source branch = one script**.

```
Source (tree)            Target (tree, optional)        Outputs (trees)
─────────────            ───────────────────────        ───────────────
{0}  script A    ─┐      {0}  guid, guid, …    ─┐        {0}  ok, ok, …   (one per target)
{1}  script B    ─┼──▶   {1}  'name'           ─┼──▶     {1}  ok
{2}  script C    ─┘      (unwired = ladder)     ┘        {2}  ok
      │                         │                              │
   each branch            targets that branch's           Success / Log both
   is one script          source is forged into           share the Source
                                                          branch paths
```

- **Source** fans out: N branches → N (or more) forged components in one pass.
- **Target** pairs *to* Source — per branch — and each branch may name **several**
  targets, so one script updates many components at once.
- **Both outputs share one path scheme**, so `Success` and `Log` line up
  branch-for-branch and can be wired on together with no path juggling. Paths
  mirror Source's, except that a branch of several file paths grafts (below).

A single un-treed input (one panel, one guid) is just the one-branch case — you
never have to think about trees until you want the fan-out.

---

## Inputs

### `Source` — string, **tree access**

The scripts to forge, **one branch per script**. A branch's content can be:

- **Source text** — a multiline string, or a **list of lines** (e.g. straight
  from a *Read File* component). The branch's items are joined with newlines
  into one script.
- **A file path** — a single line ending in `.cs` or `.py`. The forge reads the
  file itself; no *Read File* needed.
- **A list of paths** — several path lines (or one multiline panel, one path per
  line). This **expands** to one component per path, exactly as if the list were
  grafted.

Detection is by shape (a path is a lone `.cs`/`.py` line; real source is
multiline and never all-path-looking), so text branches and path branches can
mix freely in one tree — there is no mode switch. Relative paths resolve against
the folder of the **saved `.gh`**; an unsaved document errors on relative paths.
Files are read **only while Run is true**, so path sources cost nothing at rest
(unlike a native *Read File*, which re-reads every solve).

**Editing a file re-forges it immediately.** While `Run` is true the forge
*watches* every file-path source on disk: save the file in your editor and it
re-forges a moment later, with no second press and no *Read File* rig. Write,
save, watch the component change. (The watch is on each containing folder,
filtered down to the exact paths and debounced, so editors that save via a
temp file and a rename are caught too; setting `Run` false tears every watcher
down.)

What the re-forge *does* depends on the slot: a **pinned** slot (a wired
`Target`, or a header `instanceGuid`) updates its component in place, while an
**untargeted** create-new slot forges a *new* component per distinct source and
leaves the previous one on the canvas. Pin a slot when an edit must update the
same component. A **missing file** errors only its own branch (the others forge
normally); a branch that is *almost* all paths — one odd line among good ones —
warns about a likely typo'd file name before treating the whole branch as
source text.

**Access is `tree` deliberately.** The component loops the branches itself in a
single solve rather than letting GH iterate, so no output path ever carries GH's
iteration index: a branch holding **one** script reports on that branch's own
path, unchanged ({i} in → {i} out). Feeding it a flat list still works — it's
just a single branch `{0}`.

> **A path list is the one exception, and it grafts.** A branch holding
> *several* file paths is N scripts, not one, so it expands exactly as a
> **Graft** would — the item index is appended, and one branch `{0}` of three
> paths reports on `{0;0}`, `{0;1}`, `{0;2}`. That trailing index is the path's
> position **within its branch**, not GH's implicit-iteration index; the two
> shapes look identical and mean different things. A branch holding a *single*
> path does not expand. The sub-index is what keeps `{0}` of three paths
> distinguishable from three separate branches `{0}` `{1}` `{2}` — and what
> generalizes when both happen at once. **Simplify** the output to collapse a
> single source branch back to `{0}` `{1}` `{2}`.

### `Target` — object, **tree access** (optional)

Which component(s) each Source branch should update — or, left unwired, an
instruction to create new. Whether `Target` is **wired at all** is itself
meaningful, so the three states are distinct:

| Target state | Meaning |
|---|---|
| **Unwired** | Walk the **identity ladder** — the header's `instanceGuid`, else a component matching the header's own name, else forge a brand-new one next to the forge. |
| **Wired, with items for a branch** | Update those targets with that branch's source. |
| **Wired, but zero items for a branch** | **Skip** that branch — nothing forged, nothing changed. |

Each Target **item** may be:

- **A component reference** — an instance `Guid`, a guid string, or component goo
  (e.g. from Metahopper). Updates that exact component.
- **Null or an empty/whitespace item** — the *create-new* marker; forges a new
  component in that slot, **always**, even when the source's header pins an
  `instanceGuid` or names a component already on the canvas. (One list can mix
  updates and creates.) This is the deliberate escape hatch for making a second
  copy of a pinned script — see rule 4 below.
- **A keyword** — `name`, `nickname`, `name+create`, or `nickname+create`
  (case-insensitive, the whole item). The source is forged into **every**
  on-canvas stock script component of its own language whose Name (or NickName)
  matches the *source header's* own name. No match → skip, unless `+create`,
  which forges one new component. This replaces wiring a guid-lookup rig for the
  common "update all instances of this script" case.

**How a branch finds its targets** (first rule that applies wins):

1. A Target branch with the **identical path** → **all** of its items (the
   source is forged into each — this is the many-targets fan-out).
2. Else, if Target is a **single flat list**:
   - several Source branches → the item at the branch's **position** (branch 0 ↔
     item 0, …);
   - one Source branch → **every** item targets it;
   - a flat list of **only keywords and/or null/empty items** →
     **broadcasts** to every branch (each branch re-resolves the keyword against
     its own header; a null forges one new component per branch). This is what
     lets a **single** `name` item update every instance of N different scripts
     in one pass.
3. Target wired but **resolving to nothing** for a branch (empty matching
   branch, flat list ran out, no matching branch, keyword matched nothing) →
   **skip**.
4. `Target` is **unwired** → walk the **identity ladder** (below). An explicitly
   **empty item** on a wired `Target` skips the ladder entirely and always
   creates.

### The identity ladder

With `Target` unwired, each branch resolves its own target from the source
alone, **fresh on every press** — the forge remembers nothing between presses.
The first rung that answers wins:

| # | Rung | Result |
|---|---|---|
| 1 | A wired `Target` | that component (the ladder is not consulted) |
| 2 | The header's `instanceGuid` | that component, created and pinned to the guid if it is not on the canvas |
| 3 | A component whose **Name** matches the header's `name` | that component — **all** of them, if several match |
| 4 | Nothing matched | forge a new component |

Rung 3 is what makes a press **idempotent**: forge twice with `Target` unwired
and you get **one** component, because the second press finds the first by the
very name the first press stamped on it. Delete the component and press again
and it comes back. There is no hidden bookkeeping making that work and none to
go stale — the canvas itself is the record.

> **"Empty" means zero data items, not a blank panel.** A blank/whitespace panel
> still emits **one** empty string, which reads as the *create-new* marker and
> forges a **new** component. To force a skip, feed the branch **zero** items.

The same component may not be targeted twice (across branches or within one
branch); the second claim errors that slot only and is skipped. The forge also
refuses its own `InstanceGuid` — it can't forge itself.

### `Run` — bool, item access

`Run` is read two ways at once, and the same value does both jobs:

| Reading | What it does |
|---|---|
| **Edge** — `Run` goes false → true | **Push now**, every branch |
| **Level** — `Run` is true | **Arm the file watchers** on file-path sources |

A push happens on the *transition*, so holding `Run` true does not keep
forging — it forges once and then simply watches. Nothing about what is *wired*
to `Run` is inspected: a momentary **Button**, a **Boolean Toggle**, a relay, a
*Gate*, an expression and a script output all behave identically.

The forge does not compare anything before pushing. A press always pushes, and
the target it pushes into is re-resolved from scratch (see the identity ladder
above). Pressing twice in a row is therefore two real pushes into the same
component, not a push and a no-op — harmless, just not free.

**Editing a forged component directly is fine.** The next press overwrites it
from `Source`, because `Source` is the source of truth and nothing is compared.

**The first value after a load is a baseline, not an edge.** Opening a `.gh`
whose toggle was left on arms the watchers and pushes nothing. The same is true
of a `true` internalised on `Run` with nothing wired: it arms, and it can never
transition, so it never pushes on its own — pulse it from something real if you
want it to.

**Toggle or button?** They are genuinely different tools:

- A **toggle** *holds* `Run` true, which is what arms the file watcher — leave
  it on and every save of a file-path source re-forges by itself. It also
  forges once, at the moment you switch it on.
- A **button** forges once per press and watches nothing between presses.

Leave the toggle on for a write-save-look iteration loop; use the button when
you are rewiring the rig itself.

---

## Outputs

Both are **trees whose paths mirror the Source branches**, and both line up
**slot-for-slot**: within a branch there is one slot per target, so a branch
that fans out to 3 targets produces 3 aligned entries in each output.

### `Success` — bool, tree

One true/false per target slot: whether the forge's own **synchronous** pass for
that slot succeeded — header parsed, target resolved, push scheduled. Note the
distinction — a *target script's own* runtime error does **not** flip Success
false; the forge did its job. Skipped branches stay empty.

### `Log` — string, tree

A step-by-step report per Source branch — sectioned per target when a branch has
several — covering the source read, how the target was resolved, the header
parse, param sync, lost/renamed wires, and the push. This is the diagnostic
surface; read it when a forge doesn't do what you expected.

**Where is the guid of a component the forge just created?** In the `Log`:
`creating new Python 3 component <guid>`. That is the documented way to get one
to paste into a header `instanceGuid`.

> **Log stops at the push.** Identity, tooltip and icon **stamping** happens a
> couple of solutions later, once the target has compiled — past the point
> anything can flow back into an output, because the forge deliberately does not
> re-solve itself afterwards. So the Log never reports the stamping or the
> target's own compile result. **Anything that goes wrong there raises the
> forge's own error bubble instead** (an icon that could not be found is a
> warning; a stamping failure is an error), and Grasshopper clears those at the
> start of the forge's next solve, which is exactly the right lifetime. The
> target's own errors show on the target, where they belong.

> **At rest** — `Run` false, or `Run` merely held true — the Log replays each
> slot's last report under a `— last run —` heading, and `Success` reports that
> run. This is what makes a momentary button legible: it springs back and
> re-solves the forge within the same click, and without the replay the Log
> would blank the instant it did.

---

## Updating in place

When a slot targets an existing component (a wired `Target`, a keyword match, or
a header `instanceGuid`), the forge updates it rather than replacing it:

- **Wires survive by param name, then by position.** Params whose names are
  unchanged keep their underlying param objects, so their wires stay put. A param
  **renamed in place** is recycled positionally — the Log reports
  `renamed X -> Y (n wire(s) kept)` — so index-style renames keep their wires
  too. Only a param that is genuinely removed loses its connections, and each
  loss is logged as `lost wire: …`. (Params end up in the header's order — see
  the [header reference](../write-scripts/header-reference.md).)
- **A hidden `out` param is fine.** The standard-output param is recognized by its
  *type*, never by name or index, so a component whose `out` is hidden
  (right-click → Standard Output/Error Parameter) updates cleanly and stays
  hidden.
- **Languages must match.** The forge updates in place, and a C# component and a
  Python component are different classes, so a source only refreshes a target of
  its **own language**. Pushing Python source at a C# component (or vice versa)
  is refused with an error; to switch languages, delete the component and forge a
  new one (its wires are not carried over).

---

## State and identity

**The forge stores nothing in the `.gh`.** No applied key, no result guid, no
hash of what it last pushed. What keeps repeated presses and document reopens
from duplicating is the identity ladder: a source declares who it is — by
`instanceGuid`, or by the `name` its own header stamps on the component — and
the forge looks that up on the canvas every time.

Two consequences worth internalizing:

- **Identity does not follow the branch *path*.** Reordering, adding or
  removing Source branches is free: a branch is matched by what its source
  *declares*, not by where it sits in the tree.
- **A source that declares nothing is a source that cannot be found again.** A
  headerless source, or a header with no `instanceGuid` and a `name` that
  matches nothing, creates a new component on every press. That is the correct
  behaviour for "make me one of these", and the reason to give anything you plan
  to iterate on a header.

A wired-but-empty branch **skipping** (rather than creating) is what makes
match-by-name and guid-lookup rigs safe: sources whose component isn't on the
canvas are quietly skipped instead of spawning strays.

> **A `.gh` last saved by Script Forge 0.3.1-beta or earlier** carries per-slot
> state in its value table — often hundreds of entries, none of them read any
> more, and saved forever until removed.
> `script-forge/tooling/clean-forge-state.cs` strips it: run it once per document
> and save.

**A header's declared `default` seeds an *empty* param only.** Where a source
declares a default value for an input, the forge writes it into that param's
persistent data — the internalized value Grasshopper hands the script when
nothing is wired in. It does this **only when the param has no persistent data
of its own**. Persistent data is yours to edit (right-click ▸ *Set Boolean*,
*Internalise data*), and a forge re-runs on every source edit, so re-stamping it
would quietly undo what you typed — several times a minute while iterating. The
cost of that rule is the other direction: a header can *seed* a default but
never *reset* one. Clearing is yours too — right-click ▸ *Destroy persistent
data*, then re-forge to pick the header's value back up.

---

## Requirements & limits

- **Rhino 8.** Targets are the modern `C# Script` / `Python 3 Script` components
  (`RhinoCodePluginGH`). The forge only updates stock components that carry
  editable source — pointing it at a compiled plugin component (or a legacy
  GHPython / old C# script component) errors, because there is no script slot to
  push into.
- **SVG icon rasterization uses macOS `sips`;** on Windows use a PNG or base64
  icon (see the [header reference](../write-scripts/header-reference.md)). Icon problems only warn — they never block a
  forge.
- Everything else is cross-platform, stock Grasshopper — no plugins.

The component lives on the **Params ▸ Util** panel of the ribbon.

---

## Typical rigs

- **One script, create then keep updating:** `Panel` (source or file path) →
  `Source`, `Button` → `Run`, `Target` unwired. Give the source a header with a
  `name` and press: the first press creates, and every press after that finds
  the same component by that name and updates it. Nothing else to wire.
- **Live editing loop:** `Panel` holding a `.cs`/`.py` **path** → `Source`,
  `Boolean Toggle` set true → `Run`. Switching the toggle on forges once and
  arms the watcher; after that, each save of the file re-forges by itself. The
  header's `name` (or an `instanceGuid`, or a wired `Target`) is what keeps
  every save landing on the *same* component.
- **Update every instance of a script:** `Source` ← the script, `Target` ← a
  panel containing `name`. No lookup rig.
- **Forge many scripts at once:** *Entwine* / grafted list / merged *Read File*
  outputs → `Source` (one branch each). Optionally a single `name` in `Target`
  broadcasts "update all instances" across all of them.
- **Update specific existing components:** wire a component-reference source
  (e.g. Metahopper's `Objects`) → `Target`, matched branch-for-branch to
  `Source`.

For anything about the source *content* — the `@component` header, param type
hints, icons, language detection, headerless behavior — see
[`header-reference.md`](../write-scripts/header-reference.md).
