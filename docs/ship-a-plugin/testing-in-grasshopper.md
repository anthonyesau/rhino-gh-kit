# Testing a canvas tool inside Grasshopper

A canvas tool — a compiled plugin that works on clusters, documents or the canvas
rather than registering components — cannot be unit-tested out of process: nearly every
line touches `GH_Cluster`, `GH_Document` or `GH_Canvas`. What works is a **test assembly
built with `dotnet build` and run inside the live Grasshopper** through a `run_csharp`
payload. Compile errors surface in the terminal, and the tests run against the real
types.

✅ Proven on a canvas tool's suite of 292 checks, passing repeatedly, Rhino 8.35 /
Platform 0.3.0. Three deliberate source bugs each failed the relevant tests and passed
again once reverted.

It builds on [gh-internals.md § Testing a canvas tool without a restart](gh-internals.md#testing-a-canvas-tool-without-a-restart)
(the `Attach` / `Detach` contract and test-loading); read that first.

## Layout

```
<tool>/
  <Tool>.csproj          the plugin — must exclude tests/
  src/*.cs
  tests/
    <Tool>Tests.csproj   an Exe that compiles ../src/*.cs alongside the tests
    test-suite.cs        top-level statements: harness, fixtures, tests
    run-tests.cs         the run_csharp loader (not compiled into anything)
```

The plugin csproj needs `<Compile Remove="tests/**" />`, or the SDK's default glob
pulls the tests into the `.gha`.

The test project references Rhino's DLLs exactly as the plugin does, with
`<Private>false</Private>` so nothing of Rhino is copied to the output — the
references bind to what Rhino already has loaded:

```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <OutputType>Exe</OutputType>
    <TargetFramework>net8.0</TargetFramework>
    <AssemblyName>MyToolTests</AssemblyName>
    <UseAppHost>false</UseAppHost>
    <AppendTargetFrameworkToOutputPath>false</AppendTargetFrameworkToOutputPath>
    <NoWarn>$(NoWarn);CS1701;CS1702;NU1701;CA1416</NoWarn>
  </PropertyGroup>
  <PropertyGroup>
    <RhinoResources Condition="'$(RhinoResources)' == ''">/Applications/Rhino 8.app/Contents/Frameworks/RhCore.framework/Resources</RhinoResources>
    <GhPlugin>$(RhinoResources)/ManagedPlugIns/GrasshopperPlugin.rhp</GhPlugin>
  </PropertyGroup>
  <ItemGroup>
    <Compile Remove="run-tests.cs" />
    <Compile Include="../src/*.cs" />
  </ItemGroup>
  <ItemGroup>
    <Reference Include="RhinoCommon">          <HintPath>$(RhinoResources)/RhinoCommon.dll</HintPath>           <Private>false</Private></Reference>
    <Reference Include="Rhino.UI">             <HintPath>$(RhinoResources)/Rhino.UI.dll</HintPath>              <Private>false</Private></Reference>
    <Reference Include="Eto">                  <HintPath>$(RhinoResources)/Eto.dll</HintPath>                   <Private>false</Private></Reference>
    <Reference Include="System.Windows.Forms"> <HintPath>$(RhinoResources)/System.Windows.Forms.dll</HintPath>  <Private>false</Private></Reference>
    <Reference Include="System.Drawing.Common"><HintPath>$(RhinoResources)/System.Drawing.Common.dll</HintPath> <Private>false</Private></Reference>
    <Reference Include="Grasshopper">          <HintPath>$(GhPlugin)/Grasshopper.dll</HintPath>                <Private>false</Private></Reference>
    <Reference Include="GH_IO">                <HintPath>$(GhPlugin)/GH_IO.dll</HintPath>                      <Private>false</Private></Reference>
  </ItemGroup>
</Project>
```

`gh_meta.py --all` scans only the root it is given, so neither it nor
`check_filenames.py` sees `tests/`. A forged runner component kept there (a Run button
and a Report panel on the canvas, say) is checked by naming it:
`gh_meta.py --check tests/<runner>.cs`. Pointing `--all --root tests` at the folder
instead flags every non-component `.cs` in it, each of which would then need a
`gh-meta: ignore` token in its first lines.

## The loader

Paste into `mcp__rhino__run_csharp` after `dotnet build <tool>/tests`:

```csharp
using System.IO;
using System.Reflection;
using System.Runtime.Loader;

var dll = "/abs/path/to/<tool>/tests/bin/Debug/MyToolTests.dll";
var alc = new AssemblyLoadContext("MyToolTests", isCollectible: true);
try
{
    var asm = alc.LoadFromStream(new MemoryStream(File.ReadAllBytes(dll)));
    asm.EntryPoint.Invoke(null, new object[] { new string[0] });
}
catch (TargetInvocationException ex) { Console.WriteLine("CRASHED: " + ex.InnerException.GetType().Name); }
finally { alc.Unload(); }
```

- **`LoadFromStream` over bytes**, not `LoadFromAssemblyPath`, so Rhino never holds the
  file and a rebuild while it runs is fine.
- **Collectible, then `Unload()`** — the context is gone from `AssemblyLoadContext.All`
  on the next call, provided the suite detached everything it attached.
- **No `Console.SetOut` redirect is needed** ✅: the test assembly's `Console.WriteLine`
  reaches the payload's result. Redirect only when the report goes somewhere else — a
  forged script component with a Report output, say.
- **Keep the report free of `Exception:`, `error CS` and `Compile Error`**, or the
  Platform drops everything before the first match
  ([rhino-mcp-platform.md](../write-scripts/rhino-mcp-platform.md#writing-a-run_csharp-payload),
  rule 5). A crash's stack trace is the usual culprit: print the exception's type name,
  or write the full report to a file in the session scratchpad and print one line.

Why not compile the tests inside the payload with `#r` and Roslyn? `run_csharp` applies
only the last `#r` directive, and more than ~2000 characters of compile errors come back
as an empty result — a separate `dotnet build` avoids both.

## Isolation

Each of these was needed, not hypothetical:

- **Refuse to run when the user's documents could be touched.** If a command under test
  sweeps every open document, refuse while any open document holds what it would act on
  (a tool that acts on clusters refuses while any open document contains one).
- **Detach the installed copy for the run and re-attach it after.** Find it by name,
  excluding the test assembly, and invoke its `Detach()` by reflection. Otherwise its
  handlers act on the test documents: the tool's own auto-sync fixed the very state a
  test expected to be stale.

  ```csharp
  var installed = AppDomain.CurrentDomain.GetAssemblies()
      .Where(a => a != typeof(MyToolPriority).Assembly)
      .Select(a => a.GetName().Name == "MyTool" ? a.GetType("MyToolPriority") : null)
      .FirstOrDefault(t => t != null);
  installed?.GetMethod("Detach")?.Invoke(null, null);
  try { /* tests */ } finally { installed?.GetMethod("Attach")?.Invoke(null, null); }
  ```
- **Snapshot and restore every setting the tests may touch — present or absent, and the
  value.** `Instances.Settings.ConstainsEntry(key)` (sic) tells which, and
  `DeleteValue(key)` restores "absent". Reset to the shipped defaults at the start of
  each test, so tests don't inherit each other's switches.
- **Restore the canvas's document** (`Instances.ActiveCanvas.Document`) if a test sets
  it — driving a context menu does.
- **One fresh `GH_Document` per fixture**, added to `Instances.DocumentServer` for one
  test, then removed and disposed in a `finally`. Report the count of test documents
  left open at the end; it should be zero.

## Fixtures

- **Round-trip any document built in code** (write it, read it back) before
  `CreateFromDocument`, or its cluster never byte-matches its own entangled copy —
  [gh-internals.md](gh-internals.md#entanglement-reaches-only-one-documents-top-level).
- **Rename hooks after wiring them.** `num.AddSource(hook)` makes the hook report the
  wired param's name straight away, so set `CustomName` / `CustomNickName` afterwards,
  or every hook is called "Number".
- **Set a value as the UI does** — build a `GH_Structure<T>` and call
  `SetPersistentData(structure)`. The item and list overloads append.
- **`AddObject` marks a document modified**, so "was not modified" can't be asserted on
  a fixture just populated.
- **Commit a cluster edit** with `cluster.DocumentModified(edited)`, where `edited` is
  `GH_Document.DuplicateDocument` of its inner document, changed. **Right-click** through
  `attrs.RespondToMouseUp` with a menu interceptor. Both are in
  [gh-internals.md § Grasshopper's own UI handlers](gh-internals.md#grasshoppers-own-ui-handlers-can-be-driven-from-code).

## The harness

Top-level statements with a handful of local functions are enough; no test framework
is loaded into Rhino:

```csharp
int passed = 0; var failures = new List<string>(); string current = "";
void Check(string name, bool ok, string detail = null)
{
    if (ok) { passed++; return; }
    failures.Add(current + " › " + name + (detail == null ? "" : "\n      " + detail));
}
void Equal<T>(string name, T expected, T actual) =>
    Check(name, EqualityComparer<T>.Default.Equals(expected, actual), "expected " + expected + ", got " + actual);
void Test(string name, Action<Scope> body)
{
    current = name;
    using var scope = new Scope();        // owns this test's documents
    try { body(scope); }
    catch (Exception ex) { Check("threw", false, ex.GetType().Name + " " + ex.Message); }
}
```

Print one `FAIL` line per failure, then a one-line summary. Start the suite with a test
that the private members the plugin reaches by reflection still exist — after a Rhino
update, that is the test that fails first and says why.

## Related

- [gh-internals.md](gh-internals.md) — the Grasshopper behaviour the tests exercise.
- [../write-scripts/rhino-mcp-platform.md](../write-scripts/rhino-mcp-platform.md) — the
  payload constraints the loader runs under.
