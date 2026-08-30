# Testing a change to Script Forge itself

**The Script Forge on your canvas is the compiled plugin, not a script
component.** Editing `script-forge.cs` changes nothing about it until you rebuild
and restart Rhino — there is no hot reload on macOS. It is a *factory only*.

So don't try to test your edit on it. Use it to forge a live **Forge-under-test**
from `script-forge.cs`, and drive that. A forged FUT is a stock `CSharpComponent`
running your working copy, which is exactly what you want to poke at.

The standing rule still applies: **a forge refuses its own `InstanceGuid`**, so
the compiled one can build a FUT but can never update itself.

## The loop

Start from a rig — [`tooling/build-forge-rig.cs`](../../tooling/build-forge-rig.cs)
builds one. Then:

**1. Forge the FUT.** Point `Source` at the absolute path of
`script-forge/script-forge.cs` and pulse `Run`.

The header pins `"instanceGuid": "41822538-1827-4da2-bf84-58074c49b3ad"`, so the
FUT lands on that Guid on the first run and is updated in place on every later
one, whatever `Target` says. (`componentGuid` is deliberately the same value but a
different claim — the compiled plugin's permanent identity. The collision is
harmless: the FUT is a `CSharpComponent`, the compiled one a `Comp_ScriptForge`.)

Expect a `missing icon` warning if your canvas is saved at the repo root: the
header's bare `"icon": "script-forge.svg"` is correct for the compiled build,
where it resolves against `script-forge/`, and comes up one segment short against
a canvas one folder up. Icon stamping is best-effort, so it is harmless — chase it
only when the session is specifically about icon stamping.

**2. Wire the FUT** to the same panel and button, leaving `Target` unwired
(unwired means "header `instanceGuid`, else forge new", which is what you want):

```
mcp__rhino__g1_connect_many(wires=[
  {"SrcId":"<panel>",  "Src":"", "DstId":"41822538-…", "Dst":"Source"},
  {"SrcId":"<button>", "Src":"", "DstId":"41822538-…", "Dst":"Run"}])
```

`Src` is `""` because a panel and a button are pure params — one output each, with
no name to address it by.

**3. Disable the compiled Forge.** Both forges now share one panel and one button,
so without this every press runs both, and the compiled one races the FUT to forge
whatever probe the panel points at. No tool sets this; `Locked` through
`mcp__rhino__run_csharp` is what right-click ▸ **Disable** does:

```csharp
var ghDoc = Grasshopper.Instances.ActiveCanvas.Document;
var compiled = (IGH_ActiveObject) ghDoc.FindObject(new Guid("<compiled forge>"), true);
compiled.Locked = true;
```

**4. Point the panel at your probe and press.** Read `Log` off the **FUT**, not
the compiled Forge.

From there it cycles: edit `script-forge.cs` → re-enable the compiled Forge →
panel back to `script-forge.cs` → press (re-forges the FUT in place) → disable
again → probe.

**Cleaning up:** delete the FUT *and* everything it forged, re-enable the compiled
Forge, clear the panel. Deleting the FUT takes its wires with it.

## What this loop cannot test

Anything that only exists in the **compiled** build — codegen output, param
registration, upgraders, ribbon placement, the embedded icon resource. A FUT is a
script component; it never goes through `gh_codegen.py`.

For those, `tooling/publish.sh --repo script-forge install` plus a Rhino restart is
the only path. Verify which binary is live by reflecting on something the build
changed — never by the `.gha`'s timestamp.

## Related

- [`known-limitations.md`](known-limitations.md)
- [`docs/ship-a-plugin/dotnet-build.md`](../../docs/ship-a-plugin/dotnet-build.md) — the compiled build
- [`docs/use-the-forge/component-reference.md`](../../docs/use-the-forge/component-reference.md) — inputs, outputs, the identity ladder
