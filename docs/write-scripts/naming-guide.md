# Naming guide — components and params

What to *write* in each name slot. The slot **mechanics** — which header key
reaches which Grasshopper surface, and why a forged script component ignores
`nickname` — are in
[header-reference.md](header-reference.md#three-name-slots-and-which-surface-each-reaches).
Read that first if you are unsure *what* you are setting; this file is about
*what to call it*.

Applies to both languages. `gh_meta.py --check` enforces the hard parts
(uniqueness, identifier legality, no `"` in a description); everything else
here is convention, and the point of writing it down is that a new component
should not need a taste decision.

## The five slots at a glance

| slot | rule | example |
|---|---|---|
| component `name` | Title Case, the full human name. Unabbreviated. | `Curve Frames` |
| component `nickname` | Abbreviation or trim, ≤ ~8 chars. | `Frames` |
| param `name` | PascalCase, one word if it can be. The tooltip title. | `BasePlane` |
| param `variableName` | PascalCase identifier. Defaults to `name` — omit it unless you need the fan. | `BasePlane` |
| param `nickname` | **One character**, capitalised, from the lexicon below. | `P` |

Omit `variableName` and let it default. Set it only for the one case it exists
for: a C# component whose input and output must show the same label but cannot
share one identifier.

## Param `nickname` — the abbreviation ladder

McNeel's own guidance, on the SDK's `AddParameter` overloads: a param `name`
should be short, *"single words are best"*; a param `nickname` shorter still,
*"single characters are best"*. Work down this ladder and stop at the first rung
that is free — uniqueness is **per side** and **case-sensitive**.

1. **First letter of the head noun, capitalised.** The *head* noun, not the
   first word: `BasePlane` → `P`, `StarDepth` → `D`, `IgnoreZeros` → `Z`,
   `PanelWidth` → `W`. Check the lexicon below for the overrides.
2. **The type's letter instead, when the name is a role word.** `Path`,
   `Location`, `Motion`, `Source` name a *job*, not a thing — so take what the
   value **is**: `Path` (a `Curve`) → `C`, `Location` (a `Point3d`) → `P`,
   `Motion` (a `Vector3d`) → `V`. Same when a feature name carries a familiar
   quantity: `Strip` (a width fraction) → `W`, `Pen` (an offset distance) →
   `D`. A concrete noun keeps rung 1 — `Prism` is `P`, not `B`.
3. **The lowercase form**, for the three quantities stock Grasshopper writes
   lowercase: a curve parameter (`t`), an index (`i`), an interpolation weight
   (`t`). Not a general-purpose collision escape — `Ratio` is `R`.
4. **Domain notation**, where the field has one: `p`/`q` for a torus knot's
   winding numbers, `m` for a superformula's lobe count, `k` for a star
   polygon's step, `dt` for an integrator step. A reader of that domain knows it
   instantly and an invented abbreviation would be worse.
5. **Letter + axis or ordinal**: `Nx`/`Ny`, `C1`/`C2`, `PointA`/`B`/`C` → `A`/`B`/`C`.
6. **Two letters, the first of each word.**

**Never rename the param to dodge a collision.** `name` is the human label and
outranks the nickname; if two params on one side genuinely both want `C`, one of
them takes a lower rung.

**Sharing across sides is fine, and often right.** A pass-through `Geometry`
input and output should both draw `G`. Only *within* one side is a duplicate a
defect — Grasshopper keeps just one of them when it rebuilds the binding.

### Lexicon

Rung 1 unless listed here. The overrides are the ones stock Grasshopper itself
breaks.

| name | nickname | why not the head noun's letter |
|---|---|---|
| `Count`, `Number`, `Samples`, `Steps`, `Total` | `N` | stock counts are `N` — `Divide Curve` takes `N` |
| `Index` | `i` | stock writes an index lowercase |
| `Parameter`, `Parameters` | `t` | the curve-parameter convention; `Divide Curve` emits `P`, `T` and `t` on one side |
| `Blend`, `Factor` — an interpolation weight over 0–1 | `t` | same convention, normalised domain |

Everything else takes its head noun's first letter: `Curve` → `C`, `Point` /
`Plane` → `P`, `Geometry` → `G`, `Brep` → `B`, `Mesh` → `M`, `Surface` → `S`,
`Radius` → `R`, `Angle` → `A`, `Scale` → `S`, `Text` → `T`, `Height` → `H`,
`Distance` → `D`, `Width` → `W`, `Seed` → `S`, `Value` → `V`, `Run` → `R`,
`Log` → `L`.

## Component `nickname`

The canvas label. Aim for eight characters or fewer, and prefer a real word
over an acronym when one fits.

| pattern | rule | example |
|---|---|---|
| two words, one distinguishing | keep the distinguishing word | `Curve Frames` → `Frames` |
| two words, both load-bearing | initial + word | `Branch Sums` → `BSums`, `Octagon Jali PY` → `OctJali` |
| a proper noun in the name | use it alone | `Kumiko Asanoha PY` → `Asanoha` |
| parenthetical qualifier | drop it | `Ring Array (Shared Labels)` → `RingArr` |
| language/variant suffix | drop it | `Point Grid PY` → `PGrid` |

Grasshopper does not enforce uniqueness across nicknames, but Script Forge's
`nickname` target keyword matches on it — so two components sharing one are
targetable only together. That is occasionally the point (see
`examples/swap-script-test.cs`, deliberately nicknamed `Tower`) and otherwise a
bug.

## Worked example

`examples/curve-divide-py.py`, matching stock `Divide Curve` slot for slot:

```json
"name":     "Curve Divide PY",
"nickname": "CDiv",

"inputs": [
  { "name": "Path",  "nickname": "C", "type": "Curve", "access": "item", … },
  { "name": "Count", "nickname": "N", "type": "int",   "access": "item", … }
],
"outputs": [
  { "name": "Points",     "nickname": "P", "type": "Point3d", "access": "list", … },
  { "name": "Parameters", "nickname": "t", "type": "double",  "access": "list", … }
]
```

`Path` → `C` by rung 2: `Path` names a job, so the nickname comes from what the
value *is* — a curve. `Parameters` → `t` by the lexicon, which is also what frees
`P` for `Points`; had it gone to rung 1 both outputs would have wanted `P`.

## Where this comes from

There is no formal GH1 convention to inherit. David Rutten, on the naming-rules
thread: *"the naming scheme is a total mess. It evolved slowly over time without
anyone enforcing any sort of consistent rules"* — strict rules are deferred to
GH2. So the rungs above are stock-derived, not quoted.

- [Grasshopper SDK — `AddGenericParameter`](https://developer.rhino3d.com/api/grasshopper/html/M_Grasshopper_Kernel_GH_Component_GH_InputParamManager_AddGenericParameter.htm)
  — the "single words / single characters" guidance.
- [McNeel forum — Grasshopper component UI naming rules](https://discourse.mcneel.com/t/grasshopper-component-ui-naming-rules/37558)
  — Rutten on the absent convention.
- [Grasshopper Docs — Divide Curve](https://grasshopperdocs.com/components/grasshoppercurve/divideCurve.html)
  — `C`, `N`, `K` → `P`, `T`, `t`, the source of rung 2.
