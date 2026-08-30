# Script Forge — known limitations

Improvements that are either **blocked by the stock script-component model** (can't
be implemented without abandoning that model) or **deferred by deliberate design
choice**. Recorded so they aren't rediscovered from scratch.

---

## Blocked by the script-component model — cannot implement as-is

Script Forge is a **stock on-canvas `CSharpComponent` hosting a `GH_ScriptInstance`
subclass**, not a compiled `GH_Component`. That is deliberate — the whole point is a
forge the user can drop on the canvas, edit in place, and re-run without the agent.
But it means the class we author against (`GH_ScriptInstance`, in `Grasshopper.dll`)
exposes only a fixed, small set of overridable hooks:

```
BeforeRunScript()  AfterRunScript()  DrawViewportWires()  DrawViewportMeshes()
get_ClippingBox()  InvokeRunScript(...)
```

Notably **absent**: any `AddedToDocument` / `RemovedFromDocument` / dispose / unload
hook. Two desirable improvements are blocked by that gap.

### 1. Deterministic teardown on component delete / document close via a lifecycle override

**Wanted:** override `RemovedFromDocument` (like ScriptParasite, a compiled plugin,
does) to dispose the file-watchers the instant the component leaves the document.

**Why blocked:** `GH_ScriptInstance` has no such override, so the on-canvas form
cannot have one, full stop.

**The compiled build could have it.** `gh_codegen.py`'s host is a real `GH_Component`
we control, forwarding a fixed set of optional `On*` **hook methods** to the script
class — so the override is reachable, it simply isn't wired up: `RemovedFromDocument`
is not in the kit's `HOOKS` table (`OnAppendMenuItems`, `OnWrite`, `OnRead`,
`OnCreateAttributes`, `OnExpireDownStreamObjects`). Adding an `OnRemovedFromDocument`
hook upstream would unlock a genuine lifecycle override on the compiled side while the
canvas build keeps the runtime-subscription workaround below — one file, both worlds,
which is the point of the hook mechanism.

The canvas is the iteration surface and can never have the override, so the workaround
stays regardless.

**What we did instead (implemented):** subscribe at runtime to
`Grasshopper.Instances.DocumentServer.DocumentRemoved` (document close) and each
watched document's `GH_Document.ObjectsDeleted` (component delete), disposing watchers
in the handlers. This reclaims both cases promptly **without** the override, and works
in the plain on-canvas form. See `SyncWatchers` / `EnsureSubscribed` /
`OnDocumentRemoved` / `OnObjectsDeleted` in `script-forge.cs`. So the *effect* is
achieved; only the *mechanism* (a clean lifecycle override) remains out of reach.

Note what the codegen path buys here: a hook keeps the single canonical source and adds
the override only to the compiled side, so reaching the real override costs nothing —
no hand-authored compiled `GH_Component`, and no loss of the re-runnable,
edit-in-place on-canvas model that is Script Forge's reason to exist.

### 2. Cleanup when Script Forge's *own* source is recompiled (re-forged)

**Wanted:** when Forge itself is re-forged (its source pushed/recompiled), dispose the
previous compiled assembly's live `FileSystemWatcher`s, debounce timers, and event
subscriptions.

**Why blocked:** a source push builds a **new** dynamic assembly and the old one's
`static` state (`_watchers`, `_subscribed`, `_subbedDocs`) is abandoned wholesale —
there is no "assembly unloading" or instance-dispose hook a `GH_ScriptInstance` can
run at that moment. The orphaned objects therefore leak until GC finalizers reclaim
them.

**Severity: low.** Recompiling Forge itself is rare and out-of-band (you re-forge the
forge deliberately, not during normal use). The orphaned artifacts are benign:

- an orphaned `FileSystemWatcher` whose `Bump`/`OnWatchedChange` still resolve the
  component by its stable `IGH_Component` reference, so at worst it triggers one extra
  solve. That solve pushes **nothing**: the changed path is recorded in the
  *old* assembly's `_watchers` table, and `DrainPending` reads the new one — so the
  re-solve sees no pending path and no rising edge, and is a plain no-op;
- an orphaned `DocumentRemoved` handler that operates on the old (now-empty) static
  table — a no-op — and in fact still *helps*, since it will dispose that old
  assembly's watchers on the next document close.

Left as a documented residual. There is no in-model fix.

---

## Deferred by deliberate choice — implementable, not done

### 3. Drop the per-solve `File.Exists` stat on the watcher hot path

`WatchPathOf` calls `File.Exists` for each file-path source on every solve where `Run`
is true (or a watcher event is pending), before `SyncWatchers`' unchanged-set
`SetEquals` check can short-circuit — a redundant stat on a pushing solve, since that
solve's `LoadSourceIfPath` already stats + reads each file. (The resolution lives in
`WatchPathOf` rather than `SyncWatchers` so the
watched set and the watcher *trigger* set are one definition, and the call is guarded so
a forge at rest stats nothing.)

**Not done because it changes behavior:** dropping the existence filter would start
creating directory watchers for *not-yet-existing* paths (a typo, or a file to be
created later). That's arguably an improvement (auto-forge once the file appears), but
it is a behavior change, not a cleanup. Revisit
if watcher-arming cost ever shows up on a network/cloud-synced folder. The clean form:
build `want` from resolved path strings only, defer existence to watcher-creation time
(a directory watcher fires `Created` regardless).

### 4. Debounce/lock micro-details

The debounce uses a `System.Threading.Timer` under `_watchLock`; ScriptParasite uses a
`CancellationTokenSource` + `Task.Delay`. Both are fine; no change warranted. Noted only
so the difference isn't mistaken for an oversight.

---

## Cross-platform caveat

Not a watcher limitation, but the one genuinely OS-specific path in Script Forge: SVG
icon rasterization shells out to macOS `/usr/bin/sips`. `.png` and `base64:` icons work
on any platform; only an `icon:` pointing at an `.svg` is macOS-only. The file-watching
feature itself is fully cross-platform (`FileSystemWatcher` has a native backend per OS
— `ReadDirectoryChangesW` on Windows, `kqueue` on macOS — and everything else is
portable BCL / RhinoCommon; verified live on macOS).
