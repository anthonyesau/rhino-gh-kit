# Compiling a script suite into a real `.gha` with `dotnet`

The kit's normal loop pushes a `.cs` onto a canvas script component (Script Forge).
This doc covers the *other* half: turning those same canonical sources into an
ordinary compiled Grasshopper plugin, without any file living in two places.

**One canonical file per component, always.** A component's source is its `.cs` at
the project root. `build/gen/` holds only generated, disposable code. The `.cs` stays
byte-for-byte pushable by Forge; the generator's only textual transform on it is the
class-declaration line.

Status: **proven, in production.** This mechanism ships `ScriptForge.gha` from
this repo's own `script-forge/script-forge.cs` (`tooling/publish.sh --repo
script-forge install` — `script-forge/` is its own project root, nested inside
the kit that also builds it), and separately ships two other compiled plugins
built on the same pipeline (one of them via `CODEGEN=0` — no `@component`
headers at all, see "Starting a new project" below). Facts marked ✅ below carry
where they were first measured — mostly the larger of the two, the suite with
the most components and the widest use of hooks/markers/upgraders — because
that provenance is still useful (which Rhino build, which mechanism); it does
not mean the fact is specific to that project. Unmarked statements are direct
consequences of the marked ones, not open questions.

**Scope: C# only.** `gh_codegen.py` ships `.cs` sources and nothing else. Python 3
Script components have no path here — the whole mechanism turns on rewriting the
`public class Script_Instance : GH_ScriptInstance` declaration into a `partial class`
the host can reach (below), and Python has no analogue: its script component is a
different runtime with a different entry contract, not a class the generator could
rename. A mixed project can still compile its C# half — the ship list is opt-in per
file, so `.py` sources are simply never picked up — but its Python components stay
script components and must be delivered some other way. Check for `.py` at the project
root **before** planning a conversion; it is the one limit no amount of header work
gets around.

---

## Why not the ScriptEditor / rhproj publish path

Rhino's ScriptEditor can publish a palette `.gh` → `.rhproj` → **File ▸ Publish**, and
it works — but it hard-couples four unrelated things to one string, `identity.name`
in the rhproj:

| surface | comes from |
|---|---|
| GH ribbon tab | `identity.name` |
| Yak package name | `identity.name` |
| assembly name + `.gha` filename | `identity.name` |
| `AddCategoryIcon` / `AddCategorySymbolName` | `identity.name` |

The rhproj has a per-script `subcat` but **no per-script `category`**. So two suites
that both want to live in, say, the **Params** tab must both be *named* `Params`,
producing two assemblies with identical assembly identity; .NET resolves the second
load to the first and only one appears. Renaming the output file does not help —
assembly identity lives in metadata, not the filename.

Compiling decouples them: the tab comes from each component's own
`category`/`subcategory`, and the package/assembly name is free.

## What the ScriptEditor actually generates (reference material only)

Worth knowing, because it validates the approach and marks its ceiling. Publishing
leaves a `src/` dump next to the `.gha`. Inside:

- An ordinary .NET project — `net48`, NuGet `RhinoCommon 8.*` + `Grasshopper 8.*`,
  `TargetExt=.gha`. Nothing exotic.
- One `ProjectComponent_<8hex>.cs` per component, ~3.5 KB, **structurally identical,
  no logic**. `RegisterInputParams`/`RegisterOutputParams` are *empty bodies*;
  `SolveInstance` forwards to `m_script.Solve(this, DA)` on a `dynamic` rehydrated by
  the RhinoCode platform. Params are built at runtime from the script data.
- **The scripts are not in the `src/` dump.** They live in an embedded managed
  resource `Plugin.Data.resources` — one base64 JSON blob per component guid carrying
  script text, params, both SVG icons, rendered PNGs, `group`, `exposure`.

So `dotnet build` on that tree as-is yields components that all error *"Scripting
platform is not ready."* It is a shim over a private, undocumented runtime contract.

**But note what it proves:** `ProjectComponent_Base` already forwards
`AppendAdditionalMenuItems`, `BeforeSolveInstance`, `AfterSolveInstance`,
`ClippingBox`, `DrawViewportWires`, `DrawViewportMeshes`, `Locked`, `Hidden`,
`AddedToDocument`, `RemovedFromDocument` to its script object. Structurally this is
the same thing codegen does — it just defers to a runtime shim instead of emitting
real param registration. **Codegen generates the version it should have generated.**

## What "native codegen" means

Grasshopper's C# Script component is a wrapper: it holds your text, compiles it at
runtime, builds params from the type hints you set in the UI, calls `RunScript`, and
pushes results out. Codegen writes that wrapper as ordinary C# instead.

The generator reads the `@component` header — which already declares every param's
name, type hint, access and description — and emits two files per component into
`build/gen/`:

- **`Script_<Slug>.g.cs`** — your source, verbatim, with exactly one line rewritten:

  ```
  public class Script_Instance : GH_ScriptInstance                              (canonical, on disk, Forge-pushable)
  internal sealed partial class Script_<Slug> : global::GHScriptKit.ScriptBase  (what dotnet compiles)
  ```

  Fully-qualified base type, so no `using` need be injected into anyone's source.

- **`Comp_<Slug>.g.cs`** — the `GH_Component` host (identity, `ComponentGuid`,
  `Exposure`, icon, param registration) *plus* a second `partial class Script_<Slug>`
  carrying the `Invoke(IGH_DataAccess)` glue that calls your untouched
  `private void RunScript(...)`. The glue lives in the same partial class precisely so
  it can reach a `private` method.

The header stops being just documentation and becomes the build's param declaration.
**`gh_meta.py --all --check`'s header↔`RunScript` drift check therefore becomes
load-bearing**, not advisory — the generator derives the call signature from the header.

### The three shim types (`tooling/templates/`)

`ScriptBase.cs`, `ScriptComponentBase.cs` and `ScriptData.cs` are copied verbatim into
`build/gen/` by `gh_codegen.py`. `ScriptData` is the marshalling layer between
`IGH_DataAccess` and the plain CLR types a `RunScript` body declares — see
[Param mapping](#param-mapping) for the rules it implements and why each one is what
it is.

`ScriptBase` is the compile-time stand-in for `GH_ScriptInstance`. It supplies only
what script bodies actually reach for off their base class. ✅ Audited across all 28
sources in the reference project, that is exactly:

| member | uses |
|---|---|
| `Component.AddRuntimeMessage` | 196 |
| `Component.Params` | 134 |
| `Print(…)` | 29 |
| `GrasshopperDocument` | 26 |
| `Component.Attributes` | 9 |
| `RhinoDocument` | 2 |
| `Iteration`, `Reflect(…)` | 0 (present for compatibility) |

Nothing is inherited from Grasshopper internals, so nothing can break under a Rhino
update. Run the audit on a new project before trusting the shim:

```bash
grep -ho "Component\.[A-Za-z_]*" *.cs | sort | uniq -c | sort -rn
for m in Print Reflect Iteration GrasshopperDocument RhinoDocument Component; do
  printf "%-22s %s\n" "$m" "$(grep -how "$m" *.cs | wc -l)"; done
```

If the `Component` total equals the sum of the `Component.<member>` counts, every use
is a member access and nothing passes `Component` around as a value.

**Better than auditing: compile the bodies alone.** Before writing any generator you
can rewrite every class-declaration line mechanically and compile the resulting
`Script_*.g.cs` files *with no host classes at all* — they only depend on `ScriptBase`.
This validates the shim across the whole suite in seconds and is the cheapest way to
de-risk the port. ✅ In the reference project all 28 compiled with 0 errors and 3
warnings (all pre-existing obsolete-RhinoCommon-API notices). Bisecting `<LangVersion>`
in the same rig is worth doing once: ✅ that suite compiles clean at **7.3**, i.e. code
written for the GH script sandbox tends to use no modern C# at all — so do not justify
a TFM choice by "newer language features" without checking.

**`Print` has no compiled analogue** — on canvas it writes to the built-in `Out` print
stream. `ScriptBase` buffers the lines instead of discarding them. ✅ In the reference
project this is behaviour-preserving because the project hides `Out` on every
component (confirmed from the palette manifest: no `out` param listed anywhere), so
the output was already invisible. **Check this per project** — if a suite exposes
`Out`, compiling silently removes a user-visible output and the generator needs to
emit a real `Out` param instead.

### Identity has to be re-stamped after a document load

⚠️ **A compiled component's Name, NickName and Description are stored in every `.gh`
that contains it, and GH pushes them back onto the instance on open — so an installed
update does not reach components already on a canvas.** `GH_InstanceDescription.Write`
serializes all three unconditionally, for the component *and* for every param;
`GH_InstanceDescription.Read` does `TryGetString` straight back into `m_name` /
`m_nickname` / `m_description`, after the constructor has already stamped the current
build's wording. The symptom is confusing in a specific way: newly placed instances are
correct (nothing deserializes over them) while existing ones keep whatever they were
saved against, and re-saving cements the stale text rather than clearing it.

`ScriptComponentBase` fixes this for every generated component. It snapshots the
assembly's wording in its constructor — params exist by then, because `GH_Component`'s
constructor calls `PostConstructor()`, which runs `RegisterInputParams` /
`RegisterOutputParams`, before the subclass body — and re-stamps it in an overridden
`Read`. This follows GH's own precedent: for a component that is not
`IGH_VariableParameterComponent`, `GH_ComponentParamServer.Read` reuses the params the
constructor made and reads into them by index, then re-applies each param's `Access`
afterwards for exactly this reason.

`Name` and `Description` are restored unconditionally — GH exposes no UI for editing
either, so a difference can only be staleness. `NickName` is user-editable (F2 on a
component, rename on a param), so `Write` also records the nickname *this build*
stamped, and `Read` refreshes only when the stored live nickname still matches that
record; a deliberate rename is never clobbered. Documents written before this shipped
carry no such record, so their nicknames are left alone — descriptions and tooltips
still repair on open. Both overrides swallow exceptions: a document that loads with
stale wording beats one that fails to load.

### ⚠️ Removing a param shifts every label after it

GH matches saved params to live params **by index**. So a build that *removes* a
param relabels every param after it: cut the first two of four outputs and the two
survivors read back wearing the removed params' nicknames. `Name` and `Description`
repair themselves (they are restored unconditionally), which is exactly what makes
this hard to spot — the component *reports* the right names to code and draws the
wrong ones on the canvas.

So `Read` also counts the archive's `param_input` / `param_output` chunks, and when
that count differs from the live one it restores the build's nicknames outright — a
label arriving through a shifted mapping cannot be evidence of a user's rename. Any
build that drops a param needs this, or its surviving params draw under the removed
ones' names in every document saved before the change.

**The check has to catch it on the first load, because saving locks it in.** Once a
shifted document is written back, `Write` records the current build's stamp beside
the wrong nickname, and every later `Read` sees what looks like a deliberate rename
and preserves it. A document already in that state needs a manual repair: copy the
nicknames off a freshly constructed instance
(`Instances.ComponentServer.EmitObjectProxy(guid).CreateInstance()`) by index, then
save.

## Every naming variation between the two surfaces

The same canonical `.cs` produces a live **script component** on the canvas and a
compiled **`GH_Component`** in the `.gha`, and the two disagree about what several
identity fields mean. This is the boundary the whole build straddles, so it is
worth having in one place.

**Params.** ✅ The identifier / `NickName` / pretty-name rows are confirmed live on 8.33:
setting a `ScriptVariableParam`'s `Name` to `Keys` while its `NickName` stays `OutKeys`
leaves `VariableName == "OutKeys"`, flips `HasPrettyName` true, makes `GetParamRepr()`
return `"Keys (OutKeys)"`, and GH then rewrites the signature to
`RunScript(List<string> Keys, ref object OutKeys)` — an input and an output labelled
`Keys` coexisting with **zero** errors. The remaining rows are decompiled from
`RhinoCodePluginGH.gha` / `Grasshopper.dll`; the `ToolTip` row was confirmed
behaviourally on 2026-08-05 — see `docs/write-scripts/identity-properties.md`.

| | script component (`ScriptVariableParam`) | compiled (`GH_Component`) |
|---|---|---|
| **drawn on the param** | `NickName` | `NickName` |
| **the C# identifier** | `NickName` (== `VariableName`), validated as a legal identifier | none — locals are generated, bound to no param field |
| **human alias** | `Name` (== `PrettyName`) — never drawn on the param; surfaces in tooltips as `Pretty (var)` | `Name` — tooltip title and search |
| **tooltip body** | `ToolTip` (durable, serialized); `Description` is regenerated from the converter every solve | `Description`, set once at registration |
| **type** | the Converter / type hint; the declared C# type is decorative | the C# declaration, compiler-enforced |
| **`Optional`** | `true` by default | must be set explicitly (see the `pManager[i].Optional` fact below) |
| header `variableName` lands in → | `NickName` | *(nowhere — identifier only, in generated C#)* |
| header `name` lands in → | `Name` (stamped by Script Forge) | `Name` **and** `NickName` |

The consequence worth internalising: **on a script param the drawn name *is* the
variable**, so a param's `name` can never reach a forged instance's canvas. That
divergence is structural, not a gap in the tooling.

**Component identity.**

| | script component | compiled |
|---|---|---|
| Name / NickName | stamped onto `GH_InstanceDescription`; both serialize | ctor args, fixed |
| Description | two slots: `IScriptComponent.set_Description` holds for the session, `BaseScriptComponent<,>.Tooltip` is what serializes and overwrites `Description` on load — write both | ctor arg, stable |
| type identity | `ComponentGuid` is `b6ba1144-…` for *every* C# script component; only `InstanceGuid` distinguishes one from another (which is what the header's `instanceGuid` names) | `ComponentGuid` from the header's `componentGuid`, unique per component |
| ribbon home | fixed (Maths ▸ Script); `category` / `subcategory` are inert | `category` / `subcategory` → tab ▸ panel |
| prominence | n/a | `exposure` → `GH_Exposure` |
| icon | `SetIconOverride` + `IconDisplayMode`, raster only | embedded resource |

**Two non-naming differences in the same family.** On canvas GH rewrites the
`RunScript` signature every solve (typed → `ref object`, inputs as well as
outputs), so declared types are decorative there and the compiler is the first
thing that enforces them — which is why the header↔signature gate has to be fatal.
And a source push (`set_Text`) regenerates param metadata on canvas, resetting
`Description` on any param whose durable `ToolTip` slot is empty, where a compiled
component's descriptions are baked in at registration.

## Verified environment facts (macOS, Rhino 8.33)

- ✅ **The `dotnet` host can be present with no SDK.** `dotnet --list-sdks` empty but
  `dotnet --list-runtimes` populated is the giveaway. Install the SDK `.pkg` into
  `/usr/local/share/dotnet` (needs `sudo installer`, admin password) — *not*
  `dotnet-install.sh` into `~/.dotnet`, which the PATH `dotnet` will not search for
  SDKs, permanently splitting the toolchain.
- ✅ **Get the csproj shape from McNeel, don't guess:** `dotnet new install
  Rhino.Templates` then `dotnet new grasshopper --version 8 -sample`. Its csproj is
  the authoritative answer on WinForms/System.Drawing for net7.0-on-macOS.
- ✅ **Cross-platform is about the TFM *suffix*, not the version.** A plain `net8.0`
  (no `-windows`) is one binary that loads on Rhino 8 for Mac and Windows alike.
  `net7.0-windows` and `net48` in the template are Windows-only; `net48` exists solely
  for Rhino 7.
- ✅ **Rhino 8.33 hosts the .NET 8 runtime** even though `RhinoCommon.dll` is itself
  compiled as `net7.0`. Check for yourself:

  ```bash
  R="/Applications/Rhino 8.app/Contents/Frameworks/RhCore.framework/Versions/A/Resources"
  cat "$R/dotnetstart.8.runtimeconfig.json"     # -> Microsoft.NETCore.App 8.0.0, rollForward LatestMinor
  strings "$R/RhinoCommon.dll" | grep -oE "\.NETCoreApp,Version=v[0-9.]+" | sort -u
  ```

  So target **`net8.0`** unless you specifically want to load on pre-8.20 Rhino
  installs; `net7.0` buys nothing else.
- ✅ **WinForms is required even if your sources never mention it.** Grasshopper's own
  API surface is full of WinForms types (`AppendAdditionalComponentMenuItems` takes a
  `ToolStripDropDown`), so compiling against `Grasshopper` needs them resolvable. On
  macOS that means `<FrameworkReference Include="Microsoft.WindowsDesktop.App.WindowsForms" />`
  plus `<EnableWindowsTargeting>true</EnableWindowsTargeting>` (Rhino 8.11+; earlier
  Rhinos need the template's `net48` reference-assembly hack instead).
- ✅ **`<CheckForOverflowUnderflow>true</CheckForOverflowUnderflow>` is mandatory, not
  optional.** The GH script sandbox compiles *checked*; without this the compiled
  build silently changes arithmetic behaviour relative to the sandbox the code was
  written and tested in.
- ✅ **Do NOT set `<GenerateAssemblyInfo>false</GenerateAssemblyInfo>`** unless you are
  hand-writing `[assembly: AssemblyVersion(...)]` yourself. The ScriptEditor's own
  csproj sets it false *because* its generated `AssemblyInfo.cs` is full of
  `[assembly:]` attributes — copy the flag without copying those attributes and the
  build silently ships **version 0.0.0.0**, which is what `GH_AssemblyInfo.AssemblyVersion`
  reports to Grasshopper's plugin list and what yak reads. A `GH_AssemblyInfo`
  *subclass* is an ordinary class and collides with nothing, so leave generation on and
  let `<Version>`/`<Title>`/`<Company>`/`<Description>` stamp the assembly. Verify with
  `strings <Name>.gha | grep -E '^[0-9]+\.[0-9]+\.'`.
- ✅ **Reuse the rhproj's `id` as `GH_AssemblyInfo.Id`** so an already-installed beta
  upgrades in place instead of appearing as a duplicate.
- ✅ **A prerelease tag reaches Grasshopper only through the *informational* version.**
  Grasshopper displays `GH_AssemblyInfo.Version`, which is virtual but whose default
  getter **delegates straight to `AssemblyVersion`** — verified 2026-08-06 by overriding
  `AssemblyVersion` alone on a runtime subclass and reading `Version` back, so overriding
  both is dead code. The trap is what you return from it: the obvious
  `Assembly.GetName().Version` is a numeric `a.b.c.d` that **cannot hold a prerelease
  tag**, so a `0.2.0-beta` build reports a bare `0.2.0.0` and the beta designation is
  invisible. Read `AssemblyInformationalVersionAttribute` instead — the SDK stamps it
  from csproj `<Version>`, keeping that the single source of truth — and trim at `+`,
  because .NET 8 appends `+<git sha>` by default:

  ```csharp
  public override string AssemblyVersion
  {
    get
    {
      var attr = GetType().Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>();
      if (attr == null || string.IsNullOrEmpty(attr.InformationalVersion))
        return GetType().Assembly.GetName().Version.ToString();   // GenerateAssemblyInfo off
      string v = attr.InformationalVersion;
      int plus = v.IndexOf('+');
      return plus < 0 ? v : v.Substring(0, plus);
    }
  }
  ```

  Note the sha is stamped at *build* time, so building before committing a version bump
  embeds the previous commit — rebuild after the bump if the stamp is meant to identify
  it. A `-beta` version is also a **prerelease** to Rhino's Package Manager, hidden
  unless "include pre-releases" is ticked; that is usually what you want, but it does
  mean a beta looks absent to anyone who has not ticked it.
- ✅ **Uninstall/disable the old published package before smoke-testing**, or its
  components collide on `ComponentGuid` with the new build. It lives at
  `~/Library/Application Support/McNeel/Rhinoceros/packages/8.0/<Name>/<version>/`;
  renaming the `.gha` to `.gha.disabled` is the reversible way to park it.
- ✅ **Script-component inputs are `Optional = true` by default — the generated hosts
  must match, and it is not cosmetic.** A non-optional GH input with nothing wired
  stops `SolveInstance` from running at all (the component just goes orange, "failed
  to collect data"). Any component that means to run with an input left unwired — a
  `Run` gate, an optional override, anything relying on a declared `"default"` — would
  be permanently stuck, and no code on it ever runs to say why. Verified by reflection
  on a live C# Script component.
  `RegisterInputParams` therefore emits one `pManager[i].Optional = …` per
  input, defaulting to `true`; a header that says `"optional": false` is asking for the
  stuck-until-wired behaviour deliberately, and gets it.
- ✅ **A declared `"default"` becomes the five-argument `Add*Parameter` overload**
  (`AddBooleanParameter(name, nick, desc, access, true)`), which is Grasshopper's own
  way of seeding a param's persistent data. Only `bool`/`int`/`double`/`string` carry
  one, because those are the only hints JSON has a scalar for — `gh_meta --check`
  rejects the rest. The overload set is *not* uniform across types (Curve, Brep, Mesh,
  Surface, Geometry and Transform have no value overload at all), so confirm by
  reflecting on `Grasshopper.Kernel.GH_Component+GH_InputParamManager` before widening
  it, not from the SDK docs page.
- ✅ **A "No Type Hint" (`object`) param UNWRAPS its goo.** Measured on canvas: a
  Panel's `GH_String` arrives as `System.String`, and a `GH_ObjectWrapper` around an
  `IGH_DocumentObject` arrives as the document object itself. That is
  `IGH_Goo.ScriptVariable()` semantics, so the adapter must call `ScriptVariable()`
  too — see the correction under [Param mapping](#param-mapping).
- ✅ **GH rewrites the `RunScript` signature for INPUTS as well as outputs.** The kit's
  C# rules note the output case (`out List<string> Keys` → `ref object Keys`); the same
  applies going in. Measured: a param hinted `string`/list handed an actual
  `List<string>` into a body declaring `List<object> Extended`, and the call
  succeeded — no cast error. **So a canonical source's declared input types are
  decorative on canvas and both spellings "work" there.** The compiled build is the
  first place the C# compiler enforces them, which is exactly why the generator's
  header↔signature cross-check has to be *fatal*: it is the only thing that can catch
  drift the canvas silently tolerates.
- ✅ **`GH_OutputParamManager` has no `AddGuidParameter`.** Enumerate the real `Add*`
  surface by reflection rather than assuming; for `Guid` use
  `AddParameter(new Param_Guid(), name, nick, desc, access)`.
- ✅ **`DataTree<T>` implements `IGH_DataTree`**, and `IGH_DataAccess.SetDataTree` has
  an `(int, IGH_DataTree)` overload — so tree *outputs* need no conversion at all.
  Tree *inputs* do: `GetDataTree` yields `GH_Structure<TGoo>`, which `ScriptData.ToTree`
  converts (preserving empty branches via `EnsurePath` — the paths are data).
- ✅ **Dark-theme icons key off `Rhino.Runtime.HostUtils.RunningInDarkMode`.** There is
  no theme API on `GH_Skin`. Rasterize `<stem>-dark.svg` alongside `<stem>.svg`, embed
  both, and pick in the `Icon` getter. GH caches whatever `Icon` returns, so a mid-session
  theme flip keeps the old bitmap until `GH_Component.DestroyIconCache` runs — same as
  every other plugin, and not worth fighting.
- **No hot reload on macOS.** `GrasshopperReloadAssemblies` + "Memory load *.GHA
  assemblies using COFF byte arrays" is the Windows trick; on macOS assemblies cannot
  be unloaded from the AppDomain and .NET Hot Reload does not apply (Rhino loads the
  assembly itself; the IDE has no hook). This is *why* the canvas/Forge path stays the
  iteration surface and compilation stays the release step — not a workaround for it.

## Extending a compiled component from its single canonical file

The structural obstacle: your source defines `Script_Instance` → `Script_<Slug>`,
while the Grasshopper component is the generated `Comp_<Slug>`. You cannot write
`public override void CreateAttributes()` in your file — wrong class. Three mechanisms
keep the one-file rule intact. **All three were exercised end to end on Rhino 8.33
(2026-07-26) before being relied on**; what follows is measured, not projected.

- **Hook methods.** The generated host forwards to optional, well-known methods on the
  script class when present — exactly how McNeel's own `ProjectComponent_Base` works.
  Convention, not configuration: the hook's presence is the trigger. Nested helper
  types (`class MyAttributes : GH_ComponentAttributes` declared *inside* your class)
  compile in both environments, so a `CreateAttributes` hook can return one.

  | declare in your `.cs` | generator emits on the host |
  |---|---|
  | `internal void OnAppendMenuItems(ToolStripDropDown menu)` | `AppendAdditionalComponentMenuItems` |
  | `internal void OnWrite(GH_IWriter writer)` | `Write` |
  | `internal void OnRead(GH_IReader reader)` | `Read` |
  | `internal IGH_Attributes OnCreateAttributes(IGH_Component owner)` | `CreateAttributes` (null → default) |
  | `internal bool OnExpireDownStreamObjects()` | `ExpireDownStreamObjects` (true → also run the default) |

  ✅ Round-tripped on `List States`: an `OnAppendMenuItems` appending one disabled item.
  Forge-pushed to the canvas the file compiled and solved with the hook **inert** —
  nothing on canvas calls it; compiled, the item showed up in Grasshopper's own
  `AppendMenuItems` output. One file, both worlds. Detection strips comments first, so
  prose naming a hook cannot trigger one. A wrong signature is a compile error naming
  the file, which is a fine way to find out.
- **Header declarations.** Anything expressible declaratively is emitted from the
  `@component` header — `marker:` (below) or `upgrade-from:` ([Upgraders](#upgraders)).
- **`#if <PROJECT>_COMPILED`** via `<DefineConstants>`, which the canvas compiler does
  not set. ✅ Verified two-sided: a probe whose `#if` branch held text that is **not
  valid C# at all** compiled on canvas and reported its `#else` branch. So the RhinoCode
  script compiler honours preprocessor directives *and* leaves the constant undefined.
  Reach for it only when a hook's body cannot compile in the script sandbox — the hook
  mechanism needs no constant, and a source free of build configuration is the better
  default. (WinForms, the obvious worry, resolves fine on canvas.) One nice property if
  you do use it: wrapping a hook makes the build fail loudly should the constant ever
  stop being defined, because the generated forwarding override would call a method that
  vanished. The generator scans raw text, so a hook inside `#if <PROJECT>_COMPILED` is
  still detected — correctly. A hook inside `#if !<PROJECT>_COMPILED` would be detected
  wrongly; don't do that.

**The one real exception** is a type inherently *shared across* components — a custom
goo and its `Param_*`, referenced in several components' signatures. That has no single
owning file by definition and needs a home in `src/<Project>/`.

### Upgraders

One header entry per retired ComponentGuid:

```json
"componentGuid": "bdf50573-5564-4e85-b400-1c88f9195d63",
"upgradeFrom":   "67b11db0-2597-47c1-8e20-24aeedf714d5"
```

emits a free-standing `IGH_UpgradeObject` into `build/gen/`. This is the structurally
hardest single-file case — a type nothing in your source references, that Grasshopper
has to find on its own — and it works: `GH_ComponentServer.ParseGHA` loads every
`IGH_UpgradeObject` in a `.gha` at plugin-parse time. ✅ Verified end to end: the
upgrader appeared in the registry, a `.gh` carrying the retired guid round-tripped
through save and reopen, and the swap replaced the old component and migrated the
downstream wire to the new one's output. No placeholder, no dropped wire.

⚠️ **Know what a Grasshopper upgrader actually does before designing around one — the
intuition is wrong.** Measured on 8.33 by IL-scanning every caller:

- **It does not run when a document opens.** `IGH_UpgradeObject.Upgrade` is called from
  exactly one place in all of Grasshopper: `GH_DocumentEditor.MnuUpgradeComponentsClick`,
  i.e. the **Solution ▸ Upgrade Components** menu. Upgrading is user-initiated, always.
  (`GH_ComponentServer.IsUpgrader` is what enables that menu item.)
- **The retired component must still be registered.** `Upgrade()` takes a *live*
  `IGH_DocumentObject`, and `EmitObject` never consults upgraders — so a guid simply
  deleted from the assembly loads as a missing component and no upgrader can rescue it.
  Retiring a guid means marking the old component `"exposure": "hidden"`, not removing its
  source. Of Grasshopper's own 43 stock upgraders, 42 upgrade from a still-registered
  `*_OBSOLETE` component; the one exception points at an unregistered guid and is dead
  weight.
- `Version` only breaks ties between upgraders sharing an `UpgradeFrom`, so the
  generator emits a fixed date and rejects duplicates across the ship list instead —
  Grasshopper keeps only the newest and the loser would silently never fire.

### Markers: the one genuine porting problem

Components that discover siblings by scanning a sibling's script `Text` for a tag
break when compiled — a compiled component has no `Text`. Fix without a second file:
the header already declares `marker: <tag>`, so the **generator** emits it onto the
host:

```csharp
public partial class Comp_ListStates : ScriptComponentBase, IScriptMarkers {
  public string[] Markers => new[] { "GH_StateListSync:v1" };
```

The *scanning* side changes in its own canonical `.cs` to check **both** arms. ⚠️ It
cannot say `obj is IScriptMarkers` — **`GHScriptKit.IScriptMarkers` does not exist in
the canvas script sandbox**, so naming the type directly stops the file compiling in
the very world it still has to run in. Match the interface by **full name via
reflection**, exactly as the existing code already matches
`RhinoCodePlatform.GH.IScriptComponent`:

```csharp
Type markers = obj.GetType().GetInterfaces().FirstOrDefault(
  i => i.FullName == "GHScriptKit.IScriptMarkers");
if (markers != null)
{
  PropertyInfo tagsProp = markers.GetProperty("Markers");
  string[] tags = tagsProp == null ? null : tagsProp.GetValue(obj) as string[];
  if (tags != null && Array.IndexOf(tags, ListerMarker) >= 0) return true;
}
// ...then fall through to the existing Text scan
```

Always check both arms rather than branching on "am I compiled?": a compiled writer
must still find a canvas lister and vice versa, which is the normal state of affairs
mid-migration and during Forge iteration. On canvas the interface arm never matches;
compiled, the text arm never matches. Keep the marker string built by concatenation,
per the self-match-loop hazard in the kit's C# rules.

The same applies to **non-GH** consumers. A Rhino-side Python script that located a
component by scanning `obj.Text` should gain a `getattr(obj, "Markers", None)` probe
before falling back to `NickName` — a nickname is user-editable and a poor identity.

---

## Starting a new project: boilerplate checklist

What a `rhino-gh-kit-init --dotnet` scaffold would need to produce. Only the last three
items are project-specific; everything else is mechanical.

```
<repo root>/*.cs                 canonical. Unchanged. Forge-pushable. Compiled by the generator.
icons/*.svg                      + <stem>-dark.svg light-ink variants
src/
  <Project>.sln
  <Project>/
    <Project>.csproj             net8.0, Grasshopper 8.*, TargetExt .gha, checked arithmetic
    AssemblyInfo.cs              GH_AssemblyInfo: Id, name, description, author, version
yak/manifest.yml
tooling/
  publish.conf                   the 3-5 project-specific knobs (see step 10)
  publish.sh                     3-line wrapper around $KIT/tooling/publish.sh
build/gen/                       generated .g.cs + rasterized PNGs (GITIGNORED)
```

`src/<Project>/` contains **only project-wide scaffolding — two files, neither of
which mentions an individual component.** If a per-component hand-written file ever
appears there, the one-file rule has been broken and the fix is to extend the header
grammar and the generator instead.

1. `dotnet --list-sdks`; install the SDK `.pkg` if empty (see above).
2. Scaffold the csproj from `dotnet new grasshopper --version 8 -sample`, then reduce
   to a single `net8.0` target and add: `AssemblyName`, `RootNamespace`,
   `GenerateAssemblyInfo=true` (leave it ON — see the 0.0.0.0 trap above; the template
   sets it false for a reason that does not apply to you), `CheckForOverflowUnderflow=true`,
   the WinForms `FrameworkReference`, `System.Drawing.Common`, and the `build/gen` globs:

   ```xml
   <PropertyGroup><GenDir>$(MSBuildThisFileDirectory)../../build/gen/</GenDir></PropertyGroup>
   <ItemGroup>
     <Compile Include="$(GenDir)*.cs" />
     <EmbeddedResource Include="$(GenDir)icons/*.png" LogicalName="<Project>.Icons.%(Filename).png" />
   </ItemGroup>
   ```

   Note the root `.cs` files are **not** compiled directly — each declares
   `public class Script_Instance`, so N of them in one assembly would collide.
3. `AssemblyInfo.cs` with a `GH_AssemblyInfo` subclass. Reuse a previously published
   library id if there is one.
4. **Ship list = every root `.cs` whose header carries a `componentGuid`.** Explicit opt-in,
   and it forces `ComponentGuid` to be pinned in source. Mint fresh guids from a CSPRNG
   (`uuid.uuid4()` / `uuidgen`), never by hand. Validate: every pinned value round-trips
   through `uuid.UUID()`, is `version == 4`, and `variant == RFC_4122`.

   **Migrating from a published rhproj — inherit the shipped identities, but filter
   first.** The ids live in `codes[].scripts[]` as flat
   `id` / `name` / `nickname` / `subcat` / `exposure` fields. Two rules, both learned
   the hard way:

   - **Filter on `exposure` before copying anything.** An entry with
     `exposure: "excluded"` was *never published*, so its id is not a shipped identity
     and inheriting it pins a `ComponentGuid` no user document can possibly reference.
     Only entries without that flag ever reached anyone. Everything else — including a
     source that has no entry at all — gets a fresh guid.
   - **Match by `name`, not `nickname`.** Nicknames are canvas labels and collide
     freely — a rhproj typically carries several working instances sharing one
     nickname alongside the single published component, so nickname-matching silently
     assigns a working instance's id to a real component. `name` is the published
     display name, distinct per entry, and is what the header's `name` and the
     generated `Comp_<Slug>` key on anyway.

     Names are not guaranteed unique either — three of those six entries are *named*
     `Forge` too. Name matching survives it only because the exposure filter runs first.
     **Apply both rules, then assert each source matched at most one surviving entry**
     and fail loudly on an ambiguity rather than picking one.

   **The entry count is not the component count.** `codes[]` may hold one entry per
   script, or — as in `script-forge` — a *single* entry pointing at a `.ghx` palette
   whose `scripts[]` carries all the identities. The palette-backed shape accumulates
   duplicate working instances over time, which is exactly the drift a compiled build
   removes. Reconcile against the root `.cs` files, not against the entry count, and
   treat any leftover as never-shipped.
5. `.gitignore` must cover `build/` (so `build/gen/` never lands in git) and
   `icons/*.png`.
6. Run the `ScriptBase` member audit (above) before trusting the shim, and check
   whether the project exposes `Out` anywhere.
7. `python3 "$KIT/tooling/gh_codegen.py" --list` — reports the ship list and runs every
   validation without writing anything. Fix what it flags, then drop `--list`.
8. Mark any root-level source that is **not** a component with a `gh-meta: ignore`
   comment near the top — a Rhino command script, a build helper. Otherwise
   `gh_meta.py --all --check` fails on it forever and the gate becomes unusable
   (see `docs/write-scripts/header-reference.md`, appendix).
9. `yak/manifest.yml`. **Choose the package name freely** — this is the freedom the
   whole exercise buys, so do not reflexively name it after the ribbon tab. `yak spec`
   against the built `.gha` prints a starting point inferred from `GH_AssemblyInfo`.
   Keep `version:` in step with the csproj `<Version>`: one is what the Yak server
   indexes, the other is what Grasshopper's plugin list shows, and a drift between
   them is invisible until someone tries to identify which build they are running.
10. `tooling/publish.conf` + a three-line `tooling/publish.sh` wrapper. **Do not
    write a pipeline per project** — it lives here, in `tooling/publish.sh`, and
    runs validate → generate → build → package → install → push as cumulative
    stages for every project alike. The two that existed before it was factored
    out had 57 executable lines each, of which 5 differed: a `.gha` name, two
    paths and one flag. Those are exactly what the conf now holds:

    ```bash
    SLN="src/MyPlugin.sln"
    CSPROJ="src/MyPlugin/MyPlugin.csproj"
    GHA_NAME="MyPlugin.gha"
    CODEGEN_ARGS=(--resource-prefix MyPlugin.Icons)   # optional
    PACKAGE_ICON_SVG="icons/my-plugin.svg"            # optional
    ```

    The full key list, and the reasoning behind each stage, is in that script's
    header. What the project must still decide for itself is whether `push` is ever
    allowed — write that in the project's own `CLAUDE.md`, because the pipeline
    deliberately does not know.

    Two facts the pipeline encodes, worth knowing even though it handles them:

    - **Installing means a yak package out of a private folder repository, not a
      hand-copied `.gha`.** `yak install --source <dir>` takes any ordinary
      directory as a package repository, so a plugin gets versioned, upgradeable
      installs — and `yak list` as a programmatic "which build is live" — with
      nothing published. Doing both at once is the trap: a loose
      `Libraries/<Plugin>.gha` alongside the package loads every component twice
      and collides on ComponentGuid, so the install stage parks it as
      `.gha.disabled`. (A loaded `.gha` is memory-mapped, and overwriting one in
      place has crashed Rhino — the package path sidesteps that hazard entirely by
      writing a fresh directory rather than touching the mapped file. Never `cp`
      over a `.gha` Rhino may have open.)
    - **The Package Manager entry reads from a package *source*, not from the
      installed manifest.** Author, Url, Description and the version dropdown come
      from the record a source search returns; the complete `manifest.yml` sitting
      beside the installed `.gha` is never consulted for them. So a privately
      installed package shows its name, its installed version, and four blanks
      until `$YAK_LOCAL_REPO` is added as a package source in Rhino (Package
      Manager ▸ settings ▸ package sources) — an empty version dropdown is the
      tell. That setting lives in Rhino's app settings, which a running Rhino
      rewrites on exit, so the pipeline only reminds you; it cannot set it. Date
      published stays blank either way — a folder repository has no server-side
      publish timestamp. The pipeline does gate the manifest itself: missing
      `authors:` / `description:` / `url:` is fatal, because nothing downstream
      ever complains about them.
    - **The package name is the display name, and cannot contain a space.** The yak
      manifest has no display-name key (Yak.Core's model is Name/Version/Authors/
      Description/Url/Keywords/Icon/Platform), so `name:` is what the Package
      Manager prints — which invites writing it prettily. `yak build` and `yak
      search` do accept a space; `yak install "My Plugin" <version>` prints its
      usage and bails however the argument is quoted (verified 2026-08-22, yak
      8.x). Squash it instead — `MyPlugin`, not `My Plugin`. And treat a rename as
      a **new package line, not an upgrade**: the old `packages/8.0/<old-name>/`
      directory survives it, both `.gha`s then load and collide on ComponentGuid,
      so `yak uninstall <old-name>` and delete the stale `<old-name>-*.yak` from
      `$YAK_LOCAL_REPO` before installing under the new one.
    - **Only a Rhino restart loads a new build.** The file on disk changing does not
      swap what a running instance already mapped. Budget for this: it means every
      compiled-behaviour test costs a restart, which is the single biggest workflow
      difference from script components, where a push takes effect on the next solve.
      Verify which binary is live by reflecting on something the new build changed
      (a param type, a new member) — never by the file's timestamp.

### The generator (`tooling/gh_codegen.py`)

Emits two files per shipped component into `build/gen/`, plus the three templates and
the rasterized icons. Nothing in that directory is ever hand-edited; it is `rmtree`'d
and rebuilt on every run, so a renamed or retired component cannot leave a stale
`.g.cs` compiling into the plugin forever.

| file | contents |
|---|---|
| `Script_<Slug>.g.cs` | the canonical source **verbatim**, with exactly one line rewritten |
| `Comp_<Slug>.g.cs` | the `GH_Component` host + a second `partial class Script_<Slug>` holding `Invoke(IGH_DataAccess)` |

The one rewritten line, and the only textual transform applied to canonical source:

```
public class Script_Instance : GH_ScriptInstance                                  (on disk)
internal sealed partial class Script_<Slug> : global::GHScriptKit.ScriptBase      (compiled)
```

`Invoke` lives in a `partial` of the *script* class, which is what lets it call a
`private void RunScript`. It reads inputs at **header** index order (that's what fixes
the DA indices) but emits the call in **signature** order — the two need not match.

Validation, all fatal, all before a single file is written: unknown type hint or
exposure, header↔signature drift (names, directions and types), duplicate slug,
duplicate `componentGuid`, missing or duplicated class declaration, plus everything
`gh_meta.check_meta` already reports. Getting a hard failure out of the generator is
much cheaper than getting a mysterious one out of `dotnet build` on generated code.

### Param mapping

**The header declares the parameter; the `RunScript` signature declares the local that
gets passed to it. They are two halves of the same statement and must agree.**

- Header `{ "name": "X", "type": "string", "access": "list" }` in `"inputs"` →
  `pManager.AddTextParameter(…, GH_ParamAccess.list)`.
  That is the actual GH param the user sees and wires — same as what Script Forge
  stamps on canvas, so the header stays the single source of truth in both worlds.
- Signature `List<string> X` → the local the generated `Invoke` declares, fills from
  `IGH_DataAccess`, and passes to `RunScript`.

Because the canvas types inputs from the param Converter and rewrites the signature
anyway (see Verified environment facts), a mismatch between the two is **invisible
there**. `gh_codegen.py` therefore treats its header↔signature cross-check as fatal and
names both spellings:

```
FAIL  my-component.cs: param 'Extended': header says string|list (-> List<string>)
      but RunScript declares List<object>
```

Type-hint vocabulary per `docs/write-scripts/csharp-type-hints.md`; the mapping table lives in
`HINTS` in `gh_codegen.py`. `access` → item/list/tree. Note there is no
`AddGuidParameter` — `Guid` goes through `AddParameter(new Param_Guid(), …)`.

Descriptions come from the header, so in a compiled build they are **constructor
arguments** and never move — no stamping step at all. (On canvas the equivalent
durability comes from stamping `ToolTip` rather than `Description`; see
`docs/write-scripts/identity-properties.md`. The runtime `SetDesc`/`_paramsInitialized` blocks are
redundant either way, but harmless and keep canvas parity; leave them.)

⚠️ **The header's `\n` token has to reach the generated literal as a real newline.**
A description is *stamped* onto a component at exactly one moment: on canvas that is
Script Forge's `Multiline()`, and in a compiled build it is the generated
`RegisterInputParams` call. So the resolution has to happen in `gh_codegen.py`, and it
applies to descriptions **only** — a backslash-n in a component `name` still means a
backslash and an n. Escape the token's backslash instead and the compiled tooltip
renders a literal `\n` where the canvas renders a line break. Verify after any change
here by searching the built `.gha` for the UTF-16 bytes of a real newline in a known
description.

**`object`-hinted inputs must be UNWRAPPED, not passed through as goo.** ⚠️ Reading
the source suggests otherwise, so do not infer it from there: several components
defensively handle both `GH_ObjectWrapper` and raw values, which *looks* like evidence
that the canvas hands over goo — it isn't; those branches are simply belt-and-braces
and never fire. Measured behaviour (above) is that a No-Type-Hint param
delivers `ScriptVariable()`, so `ScriptData.Unwrap` does the same. Getting this
backwards would break every duck-typed consumer in a suite while still compiling
cleanly, so **measure it, don't infer it**.

### Lifecycle

One `Script_<Slug>` per component *instance*, created in the host's constructor with
`Component` assigned; `GrasshopperDocument`/`RhinoDocument` refreshed in
`BeforeSolveInstance`, `Iteration` in `SolveInstance`. Required, not stylistic —
components commonly carry state (`_paramsInitialized`, caches) across solves.

**Do not** call `AddCategoryIcon(...)` / `AddCategorySymbolName(...)` for a tab
Grasshopper owns (Params, Curve, Mesh, …). Grasshopper has already claimed those, and
stamping over one collides with its entry.

## The daily workflow

Iterating (unchanged): edit the root `.cs` → Forge push → test on canvas.

Releasing: `tooling/publish.sh install` — `gh_meta.py --all --check` → `gh_codegen.py`
→ `dotnet build -c Release` → `yak build` → `yak install` from the private folder repo
→ restart Rhino → smoke-test. `push` is a separate, public, permanent step, and several
projects here never take it: a GitHub Release already gives versioning and upgrades.

Consequence worth naming: this build reads the root `.cs` files off disk, so no
canvas is ever a build input. A palette `.gh` is a scratchpad — there is nothing to
keep it in sync with, and nothing gates it.

## Testing the header parsers

Two independent implementations read the same `@component` grammar: `gh_meta.py`
(this kit's tooling — codegen, `publish.sh`'s validate gate, the skills) and
`Script_Instance.ParseHeader`/`WarnDriftAndQuotes` inside `script-forge.cs`
itself (what a live forge parses and warns about). Nothing enforces that they
agree except a human keeping the `SYNC:` comments on both sides honest by
hand — which is exactly the kind of drift a header↔signature mismatch already
showed can happen silently.

`script-forge/tooling/test_fixtures.py` pins the two together over
`script-forge/audit-fixtures/`, a set of deliberately valid, malformed, and
edge-case sources — this project-specific harness, not generic kit
infrastructure, so it lives under `script-forge/` rather than the kit's own
`tooling/`:

```bash
python3 script-forge/tooling/test_fixtures.py              # rebuilds everything first
python3 script-forge/tooling/test_fixtures.py --no-build    # reuse the last build
```

It runs `gh_meta.py`'s `parse_header`/`check_meta` directly (Python import, not
a subprocess, reached one level up in the kit's own `tooling/`) and, on the C#
side, reflects into the same two private methods
through `script-forge/tooling/fixture-runner/` — a dev-only console project (not a shipped
artifact, but permanent tracked source) that compiles
`gh_codegen.py`'s generated stand-in for `script-forge.cs` (the same
`GHScriptKit.ScriptBase` substitution described above) so it can find and
`Invoke` `ParseHeader`/`WarnDriftAndQuotes` without either method having any
public surface. Every fixture asserts an *outcome* (parses cleanly, throws, or
— C# only — reads as headerless) and, where the header is valid but flags
something, a *problem count* that both parsers must agree on.

**The disagreement the suite exists to pin, not paper over:** for every fixture
that parses but has something wrong with it (a bad `default`, a duplicate
`variableName`, header↔signature drift, a stray double quote), `gh_meta.py
--check` fails the build (exit 1) while the same finding, on a live forge, is a
`Log` warning and the forge proceeds anyway. That is deliberate — a stricter
gate on the path that ships a public build than on the path that lets someone
iterate on canvas — and `test_fixtures.py` asserts the split explicitly rather
than letting a future edit narrow or widen it unnoticed on just one side.

It is not wired into `publish.sh`'s validate stage: that script is shared
verbatim by every project building a `.gha` (other projects built on this
pipeline included), none of which carry an `audit-fixtures/` or a
`fixture-runner/` project of their own, so a hard call to a
project-specific test suite from generic, shared infrastructure would break
their builds. Run it by hand, or from CI in a project that has both.

## Prior art

- **[ScriptParasite](https://github.com/arendvw/ScriptParasite)** (MIT) — two-way sync
  between a GH script component and a `[Nickname].cs` on disk; auto-generates a
  `.csproj` + `.vscode/settings.json` for IntelliSense. Closest existing thing to the
  Forge half of the loop, and it validates the pattern.
- **[script-sync](https://github.com/ibois-epfl/script-sync)** (IBOIS/EPFL) — same idea
  for Rhino 8, runs C#/Python from VS Code into GH.
- **No prior art for script → `GH_Component` codegen.** Nobody has published this.
- **Yak CI is solved**: [crashcloud/yak-publish](https://github.com/crashcloud/yak-publish),
  [Paramdigma/setup-yak](https://github.com/Paramdigma/setup-yak),
  [pfmephisto/rhino-yak](https://github.com/pfmephisto/rhino-yak). `yak` reads
  `YAK_TOKEN` from the environment and has a `--ci` login flag.
