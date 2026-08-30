# Decision: read JSON with System.Text.Json, hand-roll the writer

**Reading:** use `System.Text.Json` (`JsonDocument` / `JsonElement`) with a plain
`using System.Text.Json;` — no `#r`, no `PackageReference`, no `HintPath`. See
"[Reading: System.Text.Json](#reading-systemtextjson)" for what makes it free on
both surfaces.

**Writing, and any mutable `Dictionary`/`List` state tree:** hand-write a small
`JsonWriter` rather than taking a library dependency. STJ's read-only
`JsonElement` is a reason to use it for parsing and equally a reason not to
rewrite a mutable state tree around it.

Either way, keep JSON-aware code confined to the few components that actually need
it; everything else should treat the payload as an opaque string.

## Why the writer stays hand-rolled

- **Inline-script distribution.** These are scripts pasted into individual GH C#
  Script components, not a compiled plugin. A library means a per-script
  `#r "nuget:…"` directive plus a first-solve restore dependency — or reliance on
  Rhino's bundled copy, whose version/ABI can drift across Rhino releases (per
  McNeel threads the in-box System.Text.Json reference has also been finicky for the
  script editor to resolve).
- **The cost is downstream, not the parser.** Restore reads the tree as mutable
  `Dictionary<string,object>`/`List<object>` in hundreds of sites. Newtonsoft's
  `JToken` and especially System.Text.Json's read-only `JsonElement` are different
  access models; adopting either forces a large rewrite for no functional gain.
  (STJ's `Deserialize<Dictionary<string,object>>` also leaves nested values as
  `JsonElement`, needing a custom recursive converter to match today's tree.)
- **Simple, stable, self-contained.** A correct writer is a few hundred
  lines and produced/consumed in one place (invariant culture, `"R"` round-trip
  doubles, `NaN`/`Infinity`→`null`, full escape table, surrogate pairs). JSON
  syntax doesn't change, so it isn't a maintenance sink.
- **Format-robust on read.** The writer's 2-space indent is cosmetic and any
  spec-compliant reader accepts the output, so state files stay hand-editable.
- **No consolidation win.** The writer is single-owner — there is no duplication
  to DRY up; pasting it into a second owner would only add unused code.

## Revisit the writer only if

- a concrete correctness bug surfaces in it on real state data; or
- the format grows complex enough that hand-maintenance is error-prone (strongly
  typed models with attribute-driven (de)serialization).

---

## Reading: System.Text.Json

Measured live on Rhino 8.33 / macOS arm64 (.NET 8.0.14).

The constraint that decides this: Script Forge's `@component` header must parse
**byte-identically in the GH C# script sandbox and in the compiled `.gha`** built
by `dotnet build`. `#r` is unavailable (it does not compile under `dotnet build`),
so any library has to satisfy both surfaces on its own.

### What was measured

Three throwaway script components forged onto a live canvas, plus a real
`tooling/publish.sh` build with a temporary probe method spliced into
`script-forge.cs`:

| probe | GH C# script sandbox | `dotnet build` (net8.0 `.gha`) |
|---|---|---|
| `using Newtonsoft.Json.Linq;` + `JObject.Parse` | ❌ `CS0246: the type or namespace name 'Newtonsoft' could not be found` | not reached |
| `using Newtonsoft.Json;` + `JsonConvert` | ❌ same | not reached |
| `using System.Text.Json;` + `JsonDocument.Parse` | ✅ solved, no errors | ✅ build succeeded, 0 warnings |

Newtonsoft **is** loaded in the Rhino process — `Newtonsoft.Json` 13.0.0.0 and
`Newtonsoft.Json.Rhino` 10.0.0.0, both under `RhCore.framework/Resources/` — and
that is exactly the trap: presence on disk and in the AppDomain says nothing about
the sandbox's Roslyn *reference set*, which does not include it. Static inspection
could not have answered this; only the live probe could.

System.Text.Json resolves in-sandbox as:

```
System.Text.Json, Version=8.0.0.0, PublicKeyToken=cc7b13ffcd2ddd51
  @ …/Resources/dotnet/arm64/shared/Microsoft.NETCore.App/8.0.14/System.Text.Json.dll
```

— i.e. from the **shared framework**, not a plugin-private copy.

### Why it is free on both surfaces

- **Guaranteed present.** It is part of `Microsoft.NETCore.App`, the runtime Rhino 8
  already hosts. Not a Rhino-bundled assembly that could be dropped or rev'd
  independently.
- **No version drift.** The sandbox and the `.gha` bind the same 8.0.0.0 in the same
  process, because both get it from the same shared framework.
- **Free on both surfaces.** In the sandbox, a plain `using`. In the csproj, the
  net8.0 targeting pack already carries the ref assembly — verified that the build
  emits **no** `System.Text.Json` entry in `deps.json` and copies **no** dll beside
  the `.gha`.
- **Cross-platform, which the alternative was not.** `Microsoft.NETCore.App` ships
  System.Text.Json identically on Windows and macOS, so nothing platform-specific
  enters the repo. The Newtonsoft route required a `<Reference>` with a `HintPath`
  into `/Applications/Rhino 8.app/…`, which is macOS-only and would have broken the
  Windows build outright — reason enough to reject it even had the sandbox probe
  passed.
- **Parity with the Python side.** `JsonDocument` is strict about comments and
  trailing commas and decodes `\n` / `\uXXXX` at parse time with invariant-culture
  numbers — the same contract as Python's stdlib `json`, which is the other half of
  every two-parser grammar in this kit.

### Practical notes

- `JsonDocument` is `IDisposable`; wrap it in `using`.
- `JsonDocument.Parse(string)` throws on trailing non-whitespace content. When the
  JSON is embedded in something larger (a comment block), strip the wrapper first,
  or use `JsonDocument.TryParseValue(ref Utf8JsonReader, out …)` — the exact analogue
  of Python's `json.JSONDecoder().raw_decode`, which stops at the end of the value.
- Duplicate object keys are accepted last-wins, matching Python's `json`.

### One thing not verified

The probes ran on macOS only. The sandbox's reference set comes from
`Rhino.Runtime.Code.Languages.Roslyn.dll`, a managed assembly with no
platform-conditional ref pack, so the Windows sandbox is expected to resolve
System.Text.Json identically — but that is inference, not measurement, and it
has not been confirmed on Rhino 8 for Windows.
