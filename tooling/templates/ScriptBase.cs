// gh-script-kit template — copied verbatim into build/gen/ by tooling/gh_codegen.py.
// Generated code references this by its fully-qualified name, so the copied file
// needs no `using` added to any script source.
//
// This is the compile-time stand-in for Grasshopper's GH_ScriptInstance. A script
// source's canonical class declaration
//
//     public class Script_Instance : GH_ScriptInstance
//
// is rewritten by the generator to
//
//     internal sealed partial class Script_<Slug> : global::GHScriptKit.ScriptBase
//
// and everything else in the body is compiled verbatim. So this type must supply
// exactly the members a script body reaches for off its base class — no more, and
// nothing inherited from Grasshopper internals, so a Rhino update cannot break it.

using System;
using System.Collections.Generic;

using Grasshopper.Kernel;

using Rhino;

namespace GHScriptKit
{
  public abstract class ScriptBase
  {
    /// The component hosting this script. Assigned once at construction.
    public IGH_Component Component { get; internal set; }

    /// Refreshed before every solve — a script instance outlives any one document.
    public GH_Document GrasshopperDocument { get; internal set; }
    public RhinoDoc RhinoDocument { get; internal set; }
    public int Iteration { get; internal set; }

    // On canvas, Print() writes to the built-in `Out` print stream. A compiled
    // component has no such param, so the lines are buffered here instead: the
    // host clears the buffer each solve and may surface it (command line, a
    // runtime remark) if a component ever wants to. Buffering rather than
    // discarding keeps the calls meaningful and costs nothing.
    readonly List<string> _printed = new List<string>();

    public IReadOnlyList<string> PrintedLines => _printed;

    internal void ClearPrinted() => _printed.Clear();

    protected void Print(string text) => _printed.Add(text ?? string.Empty);

    protected void Print(string format, params object[] args) =>
      _printed.Add(string.Format(format, args));

    // Present only so a body that calls them still compiles; the canvas versions
    // dump object members into the `Out` stream, which has no compiled analogue.
    protected void Reflect(object obj) { }
    protected void Reflect(object obj, string memberName) { }
  }
}
