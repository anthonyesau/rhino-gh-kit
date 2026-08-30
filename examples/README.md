# Script Forge — worked examples

Open **`script-forge-examples.gh`** (tracked, sitting in this folder) in
Rhino 8 Grasshopper.

**The canvas must stay in this folder.** Its examples read the `.cs` / `.py`
files beside it by bare filename, and those files' headers point at `icons/`.
Both resolve against the folder the `.gh` is *saved* in, so moving the canvas on
its own breaks the file-path and icon examples.

You need Script Forge installed (it lives on the **Params ▸ Util** ribbon
panel). Everything else is stock Grasshopper — no other plugins.

**Script Forge 0.4.0-beta or newer.** The canvas is built for it: `Run` forges on
the rising edge (press the button once — holding it does nothing extra), the forge
has three inputs and two outputs, and a source with no `Target` finds the component
it made last time by its header `name`. Opening a canvas saved by 0.3.1-beta or
earlier drops its wires to the `Force`, `ComponentId` and `Objects` params and
carries dead per-slot state; `../script-forge/tooling/clean-forge-state.cs`
strips the latter.

## What's in the canvas

Ten sections, stacked top to bottom. Every one is self-contained — its own
forge, its own button, its own sources and targets — so you can work through
them in any order, or delete the ones you don't need.

| # | Shows | Reads |
|---|---|---|
| 1 | Forging from source text pasted into a panel | — |
| 2 | Forging from file paths; a list of paths → one component each | `kumiko-asanoha-py.py`, `octagon-jali-py.py`, `forge-demo-cs.cs` |
| 3 | The file watcher — switch Run on to arm it, then save the file and the component rebuilds | `watch-me-py.py` |
| 4 | `Target` as a guid: updating one specific component, wires kept | — |
| 5 | `Target` as the keyword `name`: update every instance, no lookup rig | — |
| 6 | A header `instanceGuid`: pinning a source to the one component it owns — rung 2 of the identity ladder | `pinned-spiral-py.py` |
| 7 | `default` and `optional` on inputs | `defaults-demo-cs.cs` |
| 8 | `name` vs `variableName` — the two label slots on a script param | `labels-demo-cs.cs` |
| 9 | What a failed forge looks like in `Log` and `Success` | `good-source-py.py`, `broken-header-py.py`, `broken-body-py.py` |
| — | A blank scratch rig at the bottom, for your own source | — |

Sections 1, 4 and 5 paste their source into a panel rather than reading a file;
their text is kept in step with `superformula-py.py`, `kumiko-asanoha-py.py`,
`octagon-jali-py.py`, `mystery.cs` and `mystery-surprise.cs`.

## The demo-only files

Most of this folder is ordinary example components. Seven files exist purely to
drive the canvas above:

- `broken-header-py.py` and `broken-body-py.py` are **deliberately malformed** —
  they are section 9's fixtures. `broken-header-py.py` carries a
  `gh-meta: ignore` token so the header check skips it; without that,
  `gh_meta.py --all --check` would fail on it by design.
- `pinned-spiral-py.py` carries an `instanceGuid` in its header naming the
  component in section 6. Forge it into a different document and you should
  delete that line first, or edit it to the guid you want.
- `watch-me-py.py` is section 3's edit-me-and-save target.
- `good-source-py.py`, `defaults-demo-cs.cs` and `labels-demo-cs.cs` are the
  clean counterparts for sections 9, 7 and 8.

## Icon paths

Every header here says the bare `icons/<name>.svg`, which resolves against the
canvas — correct for `script-forge-examples.gh` in this folder. Forging one of
these sources from a canvas saved somewhere else (a forge rig at the repo root,
say) comes up one segment short and logs an `svg not found` warning; icon
stamping is best-effort, so the forge still succeeds. See
[../docs/write-scripts/icons.md](../docs/write-scripts/icons.md).

## Reference

- [../docs/use-the-forge/script-forge.md](../docs/use-the-forge/script-forge.md) — the component: inputs, outputs, the tree model, targeting
- [../docs/use-the-forge/component-reference.md](../docs/use-the-forge/component-reference.md) — every input and output in detail
- [../docs/write-scripts/header-reference.md](../docs/write-scripts/header-reference.md) — the `@component` header grammar
