# The Rhino MCP Platform

McNeel's first-party MCP server for Rhino — the one server this kit talks to. Nothing in
the kit routes through a third-party server.

Everything below was measured on **Rhino-MCP-Platform 0.2.1-wip**, Rhino 8.33, macOS
arm64, 2026-08-13. The Platform is pre-1.0 and its tool names are still moving — McNeel's
own shipped subagent definition already references pre-`g1_` names — so **treat a Platform
upgrade as requiring a tool-name re-check**, not a transparent bump.

- Docs: <https://mcneel.github.io/RhinoMCP/>
- Rhino plugin: `RhinoMcpPlatform`, id `2668d7ed-f507-4a68-8295-8172147a0e39`

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
update** rather than hand-editing the version in.

**`MCPStart` starts the listener, and nothing works until it has been run.** The tools
being present in a session says nothing about Rhino being reachable; until `MCPStart`,
every call fails with *"Could not connect to Rhino"*. That is the normal first failure of
a session and the fix is one command from the user — **ask for it rather than working
around it.**

**The port is whatever `MCPStart` prints.** It was 10501 here, not the 10500 that appears
in older notes. Confirm with:

```bash
lsof -nP -iTCP:<port> -sTCP:LISTEN
```

> **Don't register the kit's own `.mcp.json`.** The kit ships none, deliberately. Project
> scope shadows user scope, so a project-level `rhino` entry silently hides the user's
> Platform registration and the Platform never surfaces. The tools are plainly
> `mcp__rhino__*`, with no plugin namespacing to resolve.

## The tools

The kit uses five:

| Tool | Use |
|---|---|
| `run_csharp` | Arbitrary C# against the live document. **The workhorse** — everything the kit does that isn't placing a component. |
| `g1_place_component` | Drop a component by proxy GUID (how a Script Forge gets onto a bare canvas). |
| `g1_get_canvas_graph` | Find objects and see what is wired to what. |
| `g1_solve_graph` | Force a deterministic solve. |
| `g1_search_components` | Look up a proxy GUID by name. |

The rest of the surface — `run_python`, `run_command`, `get_context`, `get_selection`,
`set_selection`, `list_objects`, `get_viewport_image`, `set_camera`, `zoom_to_*`,
`open_doc` / `save_doc` / `close_doc`, `spawn_slot` / `list_slots` / `close_slot`,
`g1_connect` / `g1_connect_many` / `g1_apply_graph` / `g1_clear_canvas` /
`g1_describe_component` / `g1_place_slider` / `g1_start`, `ask_user` — exists and works;
the kit simply has no need of it, because a `run_csharp` payload reaches further.

`g1_get_canvas_graph` samples **one item per param**, so it is for *finding* a rig, not
reading one. A multi-line panel or a Script Forge `Log` comes back truncated to its first
line. Read values through `run_csharp` and `VolatileData`.

## Writing a `run_csharp` payload

Six constraints, each of which has cost real time.

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
to see; the return shape is `{"payload":{"stdout":"…","error":null}}`. A
`ScheduleSolution` callback fires *after* the call returns, so nothing it computes can be
printed — read post-solve state in a second call.

**5. Never print the substrings `error CS`, `Compile Error` or `Exception:`.** The server
sniffs stdout for them and, on a match, moves the **entire stdout into the `error` field
and returns stdout empty**. Not a cosmetic mislabel — a successful run is reported as a
failure *and its output is gone*, because stdout was the only channel back. This is a
live hazard whenever you echo a Script Forge `Log` or any compiler diagnostics: filter or
mangle those substrings first (`"error C" + "S"` is enough).

**6. A throw rolls nothing back.** Mutations made before an exception persist, on both the
Grasshopper and the Rhino side; stdout up to the throw is kept and the exception lands in
`error`. **Make payloads idempotent, or check state before re-running one**, because a
payload that fails halfway leaves a half-applied change.

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
  has open.

## Related

- [script-forge.md](../use-the-forge/script-forge.md) — the authoring path driven over this server.
- [workflow.md](workflow.md) — the short form of the payload constraints, injected by
  the `gh-workflow-guard.sh` hook on a Grasshopper-flavored prompt.
