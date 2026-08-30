// Ring Array — copies one piece of geometry to a ring of points, rotating each
// copy outward. Wire a Polygon into Geometry for a rosette.
//
// Configures itself at solve time: param names, type hints, access, tooltips,
// component name, description and a drawn icon. Nothing to set up by hand,
// including the half no UI can reach.
//
// ONE file for both ways of running it — pasted into the editor, or read off disk
// through a `script` param. Nothing here switches on which. GhOwned() counts the
// params Grasshopper owns instead of assuming index 0 (a `script` param sits at
// input 0, the `out` print stream at output 0, either can be switched off, and
// neither exists on a freshly dropped component), and ReportSource() returns
// immediately when there is no `script` param.
//
// ⚠ On C# the linked half is theory: a C# component ignores the script param
// entirely — nothing runs, no error is reported. The handling is here so the two
// languages read the same, and because pasting is unaffected. See the README.

using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Reflection;
using Grasshopper.Kernel;
using Rhino.Geometry;

public class Script_Instance : GH_ScriptInstance
{
  const string NAME  = "Ring Array";
  const string NICK  = "RingArr";
  const string BLURB = "Copies one piece of geometry to a ring of points.";

  // name, type hint, access, tooltip
  static readonly string[,] Inputs = {
    { "Geometry", "GeometryBase", "item", "Geometry to copy. Wire a Polygon component here." },
    { "Radius",   "double",       "item", "Radius of the ring, in model units." },
    { "Count",    "int",          "item", "How many copies to place around the ring." },
  };
  // name, type hint, tooltip. An output hint is durable state on the param but
  // changes nothing downstream: the body still sees `out object`, and the emitted
  // data is identical unhinted. Set for parity with what a C# paste builds from
  // `out List<Point3d>`. NEVER do this on Python — there it is an active converter
  // that collapses a list result.
  static readonly string[,] Outputs = {
    { "Points",  "Point3d",      "The ring of points, one per copy." },
    { "Arrayed", "GeometryBase", "The input geometry copied and rotated to each point." },
  };

  // Fallback fields — see the note above. Shadowed by the real arguments from
  // pass 2 onward, which is why the compiler calls them unused.
  GeometryBase Geometry;
  double Radius;
  int Count;
  object Points, Arrayed;

  private void RunScript(GeometryBase Geometry, double Radius, int Count,
                         out object Points, out object Arrayed)
  {
    SetIdentity();
    SetParamText();
    ReportSource();

    if (!ShapeIsRight()) { BuildParams(); Points = null; Arrayed = null; return; }

    var pts = new List<Point3d>();
    var copies = new List<GeometryBase>();
    for (int i = 0; i < Count; i++)
    {
      double t = 2.0 * Math.PI * i / Math.Max(Count, 1);
      var pt = new Point3d(Radius * Math.Cos(t), Radius * Math.Sin(t), 0.0);
      pts.Add(pt);

      if (Geometry == null) continue;
      var frame = new Plane(pt, Vector3d.ZAxis);
      frame.Rotate(t, Vector3d.ZAxis, pt);
      var copy = Geometry.Duplicate();
      copy.Transform(Transform.PlaneToPlane(Plane.WorldXY, frame));
      copies.Add(copy);
    }
    Points = pts;
    Arrayed = copies;
  }

  // --- script-param awareness ---------------------------------------------------
  // How many leading params belong to Grasshopper rather than to us: the `script`
  // ScriptParam on the input side, the `out` print stream on the output side.
  // Counted, never assumed — either can be switched off from the right-click
  // menu, and neither exists on a freshly dropped component.
  static int GhOwned(List<IGH_Param> ps) =>
    ps.TakeWhile(p => p.GetType().Name != "ScriptVariableParam").Count();

  static IEnumerable<IGH_Param> Ours(List<IGH_Param> ps) => ps.Skip(GhOwned(ps));

  // Name the linked file in the component's caption, and warn if the param holds
  // more than one script. ScriptType and Grasshopper1Script are both in
  // assemblies the sandbox does not reference, so their members come off by name.
  void ReportSource()
  {
    var inputs = Component.Params.Input;
    if (inputs.Count == 0 || inputs[0].GetType().Name != "ScriptParam") return;

    var names = new List<string>();
    foreach (var goo in inputs[0].VolatileData.AllData(false))
    {
      if (goo == null) continue;
      try
      {
        var script = goo.GetType().GetProperty("Value")?.GetValue(goo);
        var n = script?.GetType().GetProperty("Name")?.GetValue(script) as string;
        if (!string.IsNullOrEmpty(n)) names.Add(n);
      }
      catch { }
    }
    if (names.Count > 1)
      Component.AddRuntimeMessage(GH_RuntimeMessageLevel.Warning,
        $"The script param holds {names.Count} scripts and Grasshopper runs every one of them. " +
        "Clear the param, then set exactly one file.");
    Component.Message = names.Count > 0 ? names[names.Count - 1] : null;
  }

  // --- component identity ------------------------------------------------------
  // Two description slots and they are not interchangeable: Description is what
  // the tooltip shows right now and is regenerated on load; Tooltip is the
  // durable slot, archived into the .gh and restored over Description on open.
  void SetIdentity()
  {
    Component.Name = NAME;
    Component.NickName = NICK;
    SetString(Component, "Description", BLURB);
    SetString(Component, "Tooltip", BLURB);

    // The canvas icon slot is a plain Bitmap, drawn at its native pixel size.
    // SetIconOverride is declared on the concrete GH_DocumentObject, not on
    // IGH_Component, so the cast is required.
    var bmp = new Bitmap(24, 24);
    using (var g = Graphics.FromImage(bmp))
    {
      g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
      g.Clear(Color.Transparent);
      using (var pen = new Pen(Color.FromArgb(220, 40, 60, 90), 2f))
        g.DrawEllipse(pen, 4, 4, 16, 16);
      for (int k = 0; k < 6; k++)
      {
        double a = 2.0 * Math.PI * k / 6;
        g.FillEllipse(Brushes.OrangeRed, (float)(11 + 8 * Math.Cos(a)), (float)(11 + 8 * Math.Sin(a)), 3, 3);
      }
    }
    ((GH_DocumentObject)Component).SetIconOverride(bmp);
    Component.IconDisplayMode = GH_IconDisplayMode.icon;
  }

  // --- per-param metadata ------------------------------------------------------
  // PrettyName is Name; ToolTip is the durable Description slot (writing ToolTip
  // writes Description too, and survives both a reopen and the next source push).
  void SetParamText()
  {
    for (int i = 0; i < Inputs.GetLength(0); i++)
    {
      var p = Component.Params.Input.FirstOrDefault(q => q.NickName == Inputs[i, 0]);
      if (p == null) continue;
      SetString(p, "PrettyName", Inputs[i, 0]);
      SetString(p, "ToolTip", Inputs[i, 3]);
      SelectTypeHint(p, Inputs[i, 1]);
      p.Access = Inputs[i, 2] == "list" ? GH_ParamAccess.list
               : Inputs[i, 2] == "tree" ? GH_ParamAccess.tree
               : GH_ParamAccess.item;
    }
    for (int i = 0; i < Outputs.GetLength(0); i++)
    {
      var p = Component.Params.Output.FirstOrDefault(q => q.NickName == Outputs[i, 0]);
      if (p == null) continue;
      SetString(p, "PrettyName", Outputs[i, 0]);
      SetString(p, "ToolTip", Outputs[i, 2]);
      SelectTypeHint(p, Outputs[i, 1]);
    }
  }

  // --- param-list surgery ------------------------------------------------------
  // A script component implements IGH_VariableParameterComponent — that is what
  // the zoom-in + / - widgets drive — so it can build its own params. Do it OUTSIDE
  // the solution, or GH 8's "object expired during a solution" guard locks the
  // canvas.
  bool ShapeIsRight()
  {
    var wantIn  = Enumerable.Range(0, Inputs.GetLength(0)).Select(i => Inputs[i, 0]);
    var wantOut = Enumerable.Range(0, Outputs.GetLength(0)).Select(i => Outputs[i, 0]);
    return Ours(Component.Params.Input).Select(p => p.NickName).SequenceEqual(wantIn)
        && Ours(Component.Params.Output).Select(p => p.NickName).SequenceEqual(wantOut);
  }

  void BuildParams()
  {
    var comp = Component;
    var vc = comp as IGH_VariableParameterComponent;
    GrasshopperDocument.ScheduleSolution(5, doc =>
    {
      Fit(comp, vc, GH_ParameterSide.Input, comp.Params.Input, Inputs);
      Fit(comp, vc, GH_ParameterSide.Output, comp.Params.Output, Outputs);
      // Hints MUST be selected here, not merely on the next solve: Grasshopper
      // rewrites RunScript's argument list from the params the moment they change,
      // and an unhinted param becomes a plain `object` argument. The body would
      // then fail to compile on `i < Count` and never get the chance to set them.
      SetParamText();
      comp.Params.OnParametersChanged();
      comp.ExpireSolution(false);
    });
  }

  static void Fit(IGH_Component comp, IGH_VariableParameterComponent vc,
                  GH_ParameterSide side, List<IGH_Param> ps, string[,] spec)
  {
    int offset = GhOwned(ps);
    int want = spec.GetLength(0) + offset;
    while (ps.Count > want) { var p = ps[ps.Count - 1]; vc.DestroyParameter(side, ps.Count - 1); comp.Params.UnregisterParameter(p); }
    while (ps.Count < want)
    {
      var p = vc.CreateParameter(side, ps.Count);
      if (side == GH_ParameterSide.Input) comp.Params.RegisterInputParam(p);
      else comp.Params.RegisterOutputParam(p);
    }
    for (int i = 0; i < spec.GetLength(0); i++)
      SetVariableName(ps[i + offset], spec[i, 0]);
  }

  // --- reflection helpers ------------------------------------------------------
  // ScriptVariableParam and BaseScriptComponent live in RhinoCodePluginGH, which
  // the script sandbox does not reference, so every slot above is reached by
  // name. DeclaredOnly plus an explicit BaseType walk finds slots declared
  // non-publicly on a base and cannot throw on a shadowed name.

  // SetVariableName, not the VariableName property: the method is what the
  // right-click rename box calls, and it re-syncs the RunScript signature at
  // once. Writing the property leaves a renamed OUTPUT unbound until the source
  // is pushed again — silently, with no error.
  static void SetVariableName(IGH_Param p, string name)
  {
    var m = p.GetType().GetMethod("SetVariableName",
              BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
    if (m != null) m.Invoke(p, new object[] { name });
    else SetString(p, "VariableName", name);
  }

  static bool SetString(object target, string name, string value)
  {
    for (var t = target.GetType(); t != null; t = t.BaseType)
    {
      var p = t.GetProperty(name, BindingFlags.Public | BindingFlags.NonPublic
                                | BindingFlags.Instance | BindingFlags.DeclaredOnly);
      if (p == null || !p.CanWrite || p.PropertyType != typeof(string)) continue;
      p.SetValue(target, value);
      return true;
    }
    return false;
  }

  // A type hint is a Converter selected on the param, not the RunScript signature.
  // An unrecognised name silently leaves the current hint in place.
  static void SelectTypeHint(IGH_Param p, string typeName)
  {
    var hp = p.GetType().GetProperty("TypeHints");
    if (hp == null || string.IsNullOrEmpty(typeName)) return;
    var hints = hp.GetValue(p);
    var select = hints.GetType().GetMethod("Select", new[] { typeof(string) });
    if (select != null) select.Invoke(hints, new object[] { typeName });
  }
}
