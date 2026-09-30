# The Rhino MCP Platform

McNeel's first-party MCP server for Rhino — the one server this kit talks to. Nothing in
the kit routes through a third-party server.

Measured on **Rhino-MCP-Platform 0.2.1-wip** (Rhino 8.33, 2026-08-13) and re-measured
where marked on **0.3.0** (Rhino 8.35, 2026-09-30), macOS arm64. The Platform is pre-1.0
and its tool names are still moving — McNeel's own shipped subagent definition already
references pre-`g1_` names — so **treat a Platform upgrade as requiring a tool-name
re-check**, not a transparent bump.

- Docs: <https://mcneel.github.io/RhinoAI/> (they do not cover slots or the router)
- Rhino plugin: `RhinoAI.rhp` in 0.3.0 (`RhinoMcpPlatform` in 0.2.x), id
  `2668d7ed-f507-4a68-8295-8172147a0e39`

## Install and connect

It ships as a **Yak package**, so install it from Rhino's package manager
(`PackageManager` → search *Rhino-MCP-Platform*). **Tick "include pre-releases"** if you
want a prerelease build rather than the latest stable. It lands in the per-user package
tree:

```
~/Library/Application Support/McNeel/Rhinoceros/packages/<rhino-version>/Rhino-MCP-Platform/<platform-version>/
  router/<os-arch>/rhino-mcp-router     ← the binary a client points at
```

Both `<rhino-version>` and `<platform-version>` are whatever is actually installed —
don't guess them. List the tree to find the real value:

```bash
ls ~/Library/Application\ Support/McNeel/Rhinoceros/packages/*/Rhino-MCP-Platform/
```

`<os-arch>` is the router build for the machine running Rhino (`osx-arm64`, `osx-x64`,
`win-x64`, …) — there is exactly one subfolder under `router/`.

Three Rhino commands come with it: **`MCPStart`**, **`MCPConnect`**, **`MCPHelp`**.

**`MCPConnect` writes the client config entry**, deriving the path above for you. The
entry is a bare command path:

```json
{ "mcpServers": { "rhino": {
    "command": "/Users/<you>/Library/Application Support/McNeel/Rhinoceros/packages/<rhino-version>/Rhino-MCP-Platform/<platform-version>/router/<os-arch>/rhino-mcp-router"
} } }
```

**That path embeds both the Platform version and the architecture, so it goes stale on
every Platform update.** A stale path surfaces as a connection error, not as "your config
is out of date" — which is a slow thing to debug. **Re-run `MCPConnect` after every
update** rather than hand-editing the version in. 0.3.0 also installs a byte-identical
router at a version-free path, `~/Library/Application Support/McNeel/Rhinoceros/ai/bin/rhino-mcp-router`,
which does not go stale.

### Slots: which Rhino a call reaches

✅ 0.3.0. The router manages **slots**, one per Rhino, each with an animal name
(`aardvark`) that persists across router restarts and Rhino restarts. `list_slots`
returns them with `port`, `pid` and `adopted` — **`adopted: true` is a Rhino the user
started** (its listener advertised itself), `false` one the router spawned.

**A call with no `slot` argument can start a second Rhino.** Every tool takes an
optional `slot`; omitted, the call goes to "the Rhino you're already working in", and
if the router knows of none it **auto-spawns a new Rhino** and reports it as
`autoSpawnedSlot` in the result. A Rhino whose listener was never started is invisible
to the router, so it does not count: one session had the user's blank Rhino open, made
a bare `get_context`, and got a freshly spawned second Rhino instead. No setting to
disable auto-spawn was found in the router's flags or strings.

**So call `list_slots` first**, before any other Rhino tool in a session — it never
spawns. An empty list means the user's Rhino is not listening: ask them to run
**`MCPStart`** in it (and confirm which Rhino they want used) rather than letting a bare
call spawn one. With several slots, pass `slot` explicitly.

The same session saw a router-spawned slot come back as a **fresh Rhino under the same
name** after the MCP servers disconnected and reconnected — Grasshopper closed, plugins
unloaded. Not reproduced; treat state in a router-spawned Rhino as disposable.

**The port is whatever the slot reports.** 10500 on 0.3.0 (both sessions measured), 10501
on 0.2.1. `list_slots` gives it; confirm with:

```bash
lsof -nP -iTCP:<port> -sTCP:LISTEN
```

> **Don't register the kit's own `.mcp.json`.** The kit ships none, deliberately. Project
> scope shadows user scope, so a project-level `rhino` entry silently hides the user's
> Platform registration and the Platform never surfaces. The tools are plainly
> `mcp__rhino__*`, with no plugin namespacing to resolve.

## The tools

The kit uses six:

| Tool | Use |
|---|---|
| `list_slots` | Which Rhinos the router can reach. **First call of a session** — see [Slots](#slots-which-rhino-a-call-reaches). |
| `run_csharp` | Arbitrary C# against the live document. **The workhorse** — everything the kit does that isn't placing a component. |
| `g1_place_component` | Drop a component by proxy GUID (how a Script Forge gets onto a bare canvas). |
| `g1_get_canvas_graph` | Find objects and see what is wired to what. |
| `g1_solve_graph` | Force a deterministic solve. |
| `g1_search_components` | Look up a proxy GUID by name. |

The rest of the surface — `run_python`, `run_command`, `get_context`, `get_selection`,
`set_selection`, `list_objects`, `get_viewport_image`, `set_camera`, `zoom_to_*`,
`open_doc` / `save_doc` / `close_doc`, `spawn_slot` / `close_slot`,
`g1_connect` / `g1_connect_many` / `g1_apply_graph` / `g1_clear_canvas` /
`g1_describe_component` / `g1_place_slider` / `g1_start`, `ask_user` — exists and works;
the kit simply has no need of it, because a `run_csharp` payload reaches further.

`g1_get_canvas_graph` samples **one item per param**, so it is for *finding* a rig, not
reading one. A multi-line panel or a Script Forge `Log` comes back truncated to its first
line. Read values through `run_csharp` and `VolatileData`.

## Writing a `run_csharp` payload

Eight constraints, each of which has cost real time.

**1. It compiles against Grasshopper — but not against the script plugin.**
`using Grasshopper; using Grasshopper.Kernel;` resolve directly; no
`AppDomain.CurrentDomain.GetAssemblies()` preamble, which older kit payloads all carried.
`RhinoCodePluginGH` (`ScriptVariableParam`, `Python3Component`, `IScriptComponent`) is
**loaded but not referenced** — `using RhinoCodePluginGH.Parameters;` fails with `CS0246`
even though the assembly is right there. Reach those types by reflection, or by
`Type.GetType("RhinoCodePluginGH.Parameters.ScriptVariableParam, RhinoCodePluginGH")`,
which does resolve.

**2. It runs on the UI thread, outside a solution.** `ManagedThreadId = 1`,
`RhinoApp.InvokeRequired = False`. So mutate inline — but **defer `ExpireSolution` into
`ghdoc.ScheduleSolution(5, …)`**. Expiring an object mid-solution trips Grasshopper 8's
*object expired during a solution* guard and locks the canvas. The scheduled solution
fires on its own between MCP calls; `g1_solve_graph` is for determinism, not necessity.

**3. `__rhino_doc__` is the injected `RhinoDoc`** — and it *is* `RhinoDoc.ActiveDoc`
(`ReferenceEquals` → true). The Grasshopper handle is a different thing entirely:
`Grasshopper.Instances.ActiveCanvas.Document`. An empty `__rhino_doc__` says nothing
about the canvas.

**4. Results come back only as scraped stdout.** `Console.WriteLine` everything you want
to see. On 0.3.0 ✅ a clean run returns `{"payload":"<stdout>"}` and a failure
`{"payload":{"error":"Failed","message":"…"}}`. A `ScheduleSolution` callback fires
*after* the call returns, so nothing it computes can be printed — read post-solve state
in a second call. Stdout has no practical length cap (12.8 KB came back whole).

**Output from an assembly the payload loads needs no capturing** ✅ 0.3.0. Its
`Console.WriteLine` shares the payload's `Console.Out` and arrives in the result, so a
loader can call `asm.EntryPoint.Invoke(…)` without a `Console.SetOut` redirect.

**5. Never print the substrings `error CS`, `Compile Error` or `Exception:`.** The server
sniffs stdout for them ✅ (each one alone, 0.3.0; a bare `Exception` does not match) and,
on a match, reports the run as `"error":"Failed"` and puts the output in `message` —
**starting at the first line that matched. Every line before it is dropped.** Not a
cosmetic mislabel: a successful run is reported as a failure and the head of its report,
usually the part that says what happened, is gone. This is a live hazard whenever you
echo a Script Forge `Log`, compiler diagnostics or a stack trace: filter or mangle those
substrings first (`"error C" + "S"` is enough). A long report is safest written to a
file in the session scratchpad, with one summary line printed.

**6. A throw rolls nothing back.** Mutations made before an exception persist, on both the
Grasshopper and the Rhino side; stdout up to the throw is kept and the exception lands in
`error`. **Make payloads idempotent, or check state before re-running one**, because a
payload that fails halfway leaves a half-applied change.

**7. More than ~2000 characters of compile errors come back as `{"payload":"\n"}`** ✅
0.3.0 — no error, no stdout, nothing to say the payload never ran. Eleven short
`CS0103` errors (1950 characters) are reported; twelve (2126) vanish, as do six long
`CS1061`s. An empty result from a payload that prints on its first line means *it did
not compile*: cut it down, or fix the first error the compiler would report, and retry.

**8. Only the last `#r` directive takes effect** ✅ 0.3.0. With two `#r` lines naming
`Microsoft.CodeAnalysis.dll` and `Microsoft.CodeAnalysis.CSharp.dll` (both loaded in Rhino
but not referenced), the payload compiles against whichever came second — swap them and
the missing reference swaps too. A payload needing two unreferenced assemblies can't get
them this way: reach the second by reflection, or build a DLL with `dotnet build` and
load it (the recipe in [testing-in-grasshopper.md](../ship-a-plugin/testing-in-grasshopper.md)).

## Benchmarking is fair here

`run_csharp` runs on the **UI thread**, so a solve triggered through it carries no
threading penalty. The hazard to check for in any other server: macOS confines a
background thread's QoS — and the worker threads inheriting it — to efficiency cores, and
an identical solve measured 98 s backgrounded against 2.3 s on the UI thread. There is no
low-QoS thread here to inherit from.

Re-measured 2026-08-13 with a script component burning a fixed CPU load, solved via
`ExpireSolution(true)`:

| trigger | wall clock |
| --- | --- |
| `run_csharp`, Rhino **backgrounded** | 3216 / 3281 ms |
| `run_csharp`, Rhino **frontmost** | 3302 / 3296 ms |
| `RhinoApp.InvokeOnUiThread`, frontmost | 3253 / 3328 ms |

All within 3%, and the same component's `Parallel.For` section ran 4 units in ~680 ms
against 2.5 s serial — real multi-core throughput, so worker threads are not confined
either. Sustained raw compute matched: 5062 ms backgrounded vs 4992 ms frontmost, with no
decay across 5 s of background running (App Nap never engaged).

**So:** numbers from a `run_csharp`-triggered solve are reportable, backgrounded or not,
and `InvokeOnUiThread` buys nothing. Still *time* it rather than assuming — and remember
the solve is synchronous on the UI thread, so a slow one blocks the call until it
finishes.

## Gotchas that look like bugs

- **`ObjectTable.Count` is not the object count.** `__rhino_doc__.Objects.Count()` takes
  the `ICollection<T>` fast path and reports the **undo buffer** — it read 4 with exactly
  one object present, and 3 on a document the enumerator said was empty. Enumerate to
  check a document is clean; don't trust `Count`.
- **`GH_Document.SolutionLocked` does not exist.** The solver switch is
  `GH_Document.EnableSolutions` (**static** and writable); `GH_Document.Enabled` is the
  per-document one. Setting `EnableSolutions = true` **solves synchronously**, so it must
  live in its own calls bracketing a payload — pasted inside one, it runs a solution
  inline and defeats the `ScheduleSolution` deferral above.
- **`GH_DocumentIO.SaveQuiet(path)` does not rebind `FilePath`.** It is the safe way to
  snapshot a document to a scratch path for a reopen test without touching what the user
  has open. To bind a document you *did* mean to save there — one you created — set
  `doc.FilePath = path; doc.IsModified = false;` afterwards: both are public setters, and
  they give it its name and a clean state.
- **Opening Grasshopper can report `"error":"Failed"` and still work.** A `.gha` that fails
  to load prints *"An error occured during GHA assembly loading … Exception
  System.IO.FileLoadException: … Assembly with same name is already loaded"* and the
  sniffer (rule 5) turns the result into a failure, though the editor opens. The cause is
  the same library in two load paths — a loose `Libraries/<Name>.gha` beside an installed
  package of it. Tell the user which file; it is their install, not the payload's.
- **A payload that opens a menu or modal dialog does not return until a person closes
  it**, and the Platform abandons the call after 300 s. A context menu appears above
  every app and closes at the first click elsewhere; an Eto dialog opens *behind* the
  frontmost app, where nobody may notice it. Rhino keeps answering meanwhile: a later
  call runs nested inside the modal loop, against a half-finished operation. Drive the
  code path behind the UI instead — recipe and details in
  [gh-internals.md](../ship-a-plugin/gh-internals.md#menus-and-dialogs-block-the-call-that-opens-them).
- **`screencapture` fails from the agent's shell** with *"could not create image from
  display"* (macOS screen-recording permission). Verify by reading state through
  `run_csharp`, or use the Platform's `get_viewport_image` for a Rhino viewport — not a
  shell screenshot of the Grasshopper window.

## Related

- [script-forge.md](../use-the-forge/script-forge.md) — the authoring path driven over this server.
- [workflow.md](workflow.md) — the short form of the payload constraints, injected by
  the `gh-workflow-guard.sh` hook on a Grasshopper-flavored prompt.
